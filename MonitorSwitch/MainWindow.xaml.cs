using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace MonitorSwitch
{
  public partial class MainWindow : Window
  {
    private readonly ClientSideUdpBroadcaster _broadcaster;
    private readonly ServerSideUdpListener _listener;
    private readonly bool _isServer;
    private IntPtr _handle;
    private NotifyIcon _notifyIcon;  //system tray icon

    public MainWindow()
    {
      InitializeComponent();
      
      _isServer = Convert.ToBoolean(ConfigurationManager.AppSettings["PhysicalKeyboardAttachedToThisComputer"]);
      var udpPort = int.Parse(ConfigurationManager.AppSettings["udpBroadcastPort"]);
      if (_isServer)
      {
        _listener = new ServerSideUdpListener(udpPort);
        _listener.MessageReceived += (s,a) =>
        {
          Console.WriteLine("udp pulse received");
          SendKeysForMouseWithoutBorders(1);
        };
      }
      else
      {
        _broadcaster = new ClientSideUdpBroadcaster(udpPort);
      }
      
      ShowInTaskbar = false;
      Loaded += OnLoaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
    {
      //add a couple global hot keys to switch between computers
      _handle = new WindowInteropHelper(this).Handle;
      var src = HwndSource.FromHwnd(_handle);
      src.AddHook(WndProc);

      RegisterHotkeys();

      _notifyIcon = new NotifyIcon();
      _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
      _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
      _notifyIcon.Text = "KVM simulator - DblClick to exit";

      Visibility = Visibility.Hidden;
      _notifyIcon.Visible = true;
    }

    private void RegisterHotkeys()
    {
      const uint ctrlAlt = (uint)(KeyModifiers.Control | KeyModifiers.Alt);

      var success = RegisterHotKey(_handle, 1, ctrlAlt, Keys.D1.GetHashCode());
      success = RegisterHotKey(_handle, 2, ctrlAlt, Keys.D2.GetHashCode());
    }

    private void NotifyIcon_DoubleClick(object sender, EventArgs eventArgs)
    {
      this.Close();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
      if (msg != WM_HOTKEY)
        return IntPtr.Zero;

      var hotkeyId = wParam.ToInt32();
      SwitchToMachine(hotkeyId);

      return IntPtr.Zero;
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
      SwitchToMachine(2);
    }

    private void SwitchToMachine(int machineToSwitchTo)
    {
      Console.WriteLine($"switching to #{machineToSwitchTo}");

      var handles = GetMonitorHandles();

      for (var i = 0; i < handles.Count; i++)
      {
        var inputType = GetInputTypeFromConfig(machineToSwitchTo, i + 1);
        SetInputType(handles[i], inputType);
      }
      
      //send keys to trigger MouseWithoutBorders.  Unforntuately, MWB only listens to the keyboard on the machine that the keyboard is physically
      //connected to.  This means switching to machine 2 is easy .. but switching back to machine1 requires a network pulse so that machine1 can 
      //send the keys to tell MWB to switch back to itself.
      if (_isServer)
        SendKeysForMouseWithoutBorders(machineToSwitchTo);
      else
        _broadcaster.SendMessageToServer();
    }

    private void SendKeysForMouseWithoutBorders(int machineToSwitchTo)
    {
      var sendKeys = GetSendKeysFromConfig(machineToSwitchTo);
      if (!string.IsNullOrEmpty(sendKeys))
        SendKeys.SendWait(sendKeys);
    }

    private InputType GetInputTypeFromConfig(int machine, int monitor)
    {
      var config = ConfigurationManager.AppSettings[$"machine{machine}_monitor{monitor}"];
      return (InputType)Enum.Parse(typeof(InputType), config);
    }

    private string GetSendKeysFromConfig(int machine)
    {
      var config = ConfigurationManager.AppSettings[$"sendKeysWhenSwitchingToMachine{machine}"];
      return config;
    }
    
    // This will return the total number of Displays that windows is drawing.  Meaning that two Mirrored monitors will report as a single display
    private List<IntPtr> GetMonitorHandles()
    {
      var hMonitors = new List<IntPtr>();
      MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref Rect prect, int d) =>
      {
        hMonitors.Add(hMonitor);
        return true;
      };

      EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, 0);
      return hMonitors;
    }

    private void SetInputType(IntPtr hMonitor, InputType inputType)
    {
      // get number of physical displays (assume only one for simplicity)
      var success = GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint physicalMonitorCount);

      var physicalMonitorArray = new Physical_Monitor[physicalMonitorCount]; //count will be 1 for extended displays and 2(or more) for mirrored displays

      success = GetPhysicalMonitorsFromHMONITOR(hMonitor, physicalMonitorCount, physicalMonitorArray);
      var physicalMonitor = physicalMonitorArray[physicalMonitorArray.Length - 1]; //if count > 1 then we assume the laptop screen is 1st in array and the mirrored monitor is 2nd

      success = SetVCPFeature(physicalMonitor.hPhysicalMonitor, INPUT_SELECT, (int)inputType);

      success = DestroyPhysicalMonitors(physicalMonitorCount, physicalMonitorArray);
    }




    private const int PHYSICAL_MONITOR_DESCRIPTION_SIZE = 128;
    private const int INPUT_SELECT = 0x60;
    private const int WM_HOTKEY = 0x0312;

    [Flags]
    private enum KeyModifiers
    {
      None = 0,
      Alt = 1,
      Control = 2,
      Shift = 4,
      WinKey = 8
    }

    private enum InputType
    {
      VGA = 1,
      DVI = 3,
      HDMI = 4,
      YPbPr = 12,
      DisplayPort = 15
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);


    [DllImport("user32")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lpRect, MonitorEnumProc callback, int dwData);

    [DllImport("User32")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, MONITOR_DEFAULTTO dwFlags);

    [DllImport("Dxva2.dll")]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("Dxva2.dll")]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] Physical_Monitor[] physicalMonitorArray);

    [DllImport("Dxva2.dll")]
    private static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [Out] Physical_Monitor[] physicalMonitorArray);

    [DllImport("Dxva2.dll")]
    private static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, int dwNewValue);

    private delegate bool MonitorEnumProc(IntPtr hDesktop, IntPtr hdc, ref Rect pRect, int dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
      public int left;
      public int top;
      public int right;
      public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Physical_Monitor
    {
      public IntPtr hPhysicalMonitor;

      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = PHYSICAL_MONITOR_DESCRIPTION_SIZE)]
      public string szPhysicalMonitorDescription;
    }

    internal enum MONITOR_DEFAULTTO
    {
      NULL = 0x00000000,
      PRIMARY = 0x00000001,
      NEAREST = 0x00000002,
    }

  }
}
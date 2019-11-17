using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MonitorSwitch
{
  public class ServerSideUdpListener
  {
    public event EventHandler MessageReceived;

    public ServerSideUdpListener(int port)
    {
      var udpClient = new UdpClient(port);
      var endpoint = new IPEndPoint(IPAddress.Any, port);

      Task.Run(() =>
      {
        while (true)
        {
          var bytes = udpClient.Receive(ref endpoint);
          var message = Encoding.ASCII.GetString(bytes, 0, bytes.Length);
          if (message == "go")
          {
            Task.Run(() =>
            {
              MessageReceived?.Invoke(this, EventArgs.Empty);
            });
          }
        }
      });
    }
  }
}

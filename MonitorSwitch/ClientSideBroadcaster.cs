using System.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MonitorSwitch
{
  public class ClientSideUdpBroadcaster
  {
    private readonly Socket _clientSideSocket;
    private readonly IPEndPoint _endpoint;

    public ClientSideUdpBroadcaster(int port)
    {
      _clientSideSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

      var broadcastAddressConfig = ConfigurationManager.AppSettings["udpBroadcastAddress"];
      var ipToBroadcastTo = IPAddress.Parse(broadcastAddressConfig);
      _endpoint = new IPEndPoint(ipToBroadcastTo, port);

    }

    public void SendMessageToServer()
    {
      var buffer = Encoding.ASCII.GetBytes("go");
      _clientSideSocket.SendTo(buffer, _endpoint);
    }
  }
}
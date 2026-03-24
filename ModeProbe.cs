using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LOLProximityVC
{
    /// <summary>
    /// Connects briefly to get PKT_MODE, then disconnects.
    /// ClientTab uses this to decide which client class to create.
    /// </summary>
    public class ModeProbe
    {
        private readonly string _server;
        private readonly int _port;
        private readonly string _champion;

        public event Action<string> OnModeReceived;
        public event Action<string> OnError;

        public ModeProbe(string server, int port, string champion)
        {
            _server = server; _port = port; _champion = champion;
        }

        public void Probe()
        {
            Task.Run(() =>
            {
                UdpClient udp = null;
                try
                {
                    udp = new UdpClient();
                    udp.Client.ReceiveTimeout = 3000;
                    udp.Connect(_server, _port);

                    byte[] name = System.Text.Encoding.UTF8.GetBytes(_champion);
                    byte[] pkt = new byte[1 + name.Length]; pkt[0] = Packets.CONNECT;
                    Buffer.BlockCopy(name, 0, pkt, 1, name.Length);
                    udp.Send(pkt, pkt.Length);

                    var ep = new IPEndPoint(IPAddress.Any, 0);

                    // Receive ACK
                    var data = udp.Receive(ref ep);
                    if (data.Length < 1 || data[0] != Packets.ACK)
                    { OnError?.Invoke("Server rejected connection"); return; }

                    // Receive MODE
                    data = udp.Receive(ref ep);
                    if (data.Length < 2 || data[0] != Packets.MODE)
                    { OnError?.Invoke("Server did not send mode"); return; }

                    string mode = System.Text.Encoding.UTF8.GetString(data, 1, data.Length - 1).Trim().ToLower();
                    Console.WriteLine($"[probe] Mode: {mode}");

                    // Disconnect cleanly so the real client can connect fresh
                    udp.Send(new byte[] { Packets.DISCONNECT }, 1);
                    OnModeReceived?.Invoke(mode);
                }
                catch (Exception ex) { OnError?.Invoke($"Could not connect: {ex.Message}"); }
                finally { udp?.Close(); }
            });
        }
    }
}
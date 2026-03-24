using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LOLProximityVC
{
    /// <summary>
    /// Discord mode client — no audio at all.
    /// Connects to server, sends Discord user ID, receives PKT_VOLUMES,
    /// applies them to local Discord via IPC.
    /// </summary>
    public class DiscordClient
    {
        private readonly string _server;
        private readonly int _port;
        private readonly string _champion;

        private UdpClient _udp;
        private CancellationTokenSource _cts;
        private bool _connected = false;
        private DiscordIpcBridge _discord = null;

        public event Action<string> OnStatusChanged;
        public event Action<string[]> OnClientsChanged;
        public event Action<string, int> OnPlayerLevel;
        public event Action<bool> OnDiscordIpcStatus;

        public DiscordClient(string server, int port, string champion)
        {
            _server = server; _port = port; _champion = champion;
        }

        public void Connect()
        {
            _cts = new CancellationTokenSource();
            _udp = new UdpClient();
            _udp.Client.ReceiveTimeout = 2000;
            Console.WriteLine($"[discord-client] Connecting to {_server}:{_port} as '{_champion}'");
            try { _udp.Connect(_server, _port); }
            catch (Exception ex) { Log($"Could not reach server: {ex.Message}"); return; }
            SendPacket(Packets.CONNECT, System.Text.Encoding.UTF8.GetBytes(_champion));
            SetStatus("Waiting for server...");
            Task.Run(() => WaitForAck(_cts.Token));
        }

        public void Disconnect()
        {
            if (_connected) SendPacket(Packets.DISCONNECT);
            _cts?.Cancel();
            _connected = false;
            _discord?.Disconnect(); _discord = null;
            _udp?.Close();
            Console.WriteLine("[discord-client] Disconnected.");
            SetStatus("Disconnected");
        }

        public void SendDiscordId(string discordId)
        {
            SendPacket(Packets.DISCORD_ID, System.Text.Encoding.UTF8.GetBytes(discordId.Trim()));
            Console.WriteLine($"[discord-client] Sent Discord ID: {discordId}");
        }

        private void WaitForAck(CancellationToken ct)
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _udp.Receive(ref ep);
                if (data.Length < 1) { Log("Invalid server response"); return; }

                if (data[0] == Packets.ACK)
                {
                    _connected = true;
                    Console.WriteLine("[discord-client] ACK received — connecting to Discord IPC");
                    SetStatus($"Connected as '{_champion}'");

                    _discord = new DiscordIpcBridge();
                    bool ok = _discord.Connect();
                    OnDiscordIpcStatus?.Invoke(ok);
                    if (!ok) { Console.WriteLine("[discord-client] WARNING: Discord IPC failed"); _discord = null; }

                    Task.Run(() => ReceiveLoop(ct));
                    Task.Run(() => KeepaliveLoop(ct));
                }
                else if (data[0] == Packets.REJECT)
                {
                    string reason = data.Length > 1 ? System.Text.Encoding.UTF8.GetString(data, 1, data.Length - 1) : "Unknown";
                    Log($"Rejected: {reason}");
                }
            }
            catch (Exception ex) { Log($"ACK wait failed: {ex.Message}"); }
        }

        private void ReceiveLoop(CancellationToken ct)
        {
            Console.WriteLine("[discord-client] Receive loop started.");
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    byte[] data = _udp.Receive(ref ep);
                    if (data.Length < 2) continue;
                    switch (data[0])
                    {
                        case Packets.VOLUMES:
                            if (_discord == null) break;
                            foreach (var pair in System.Text.Encoding.UTF8.GetString(data, 1, data.Length - 1).Split(',', StringSplitOptions.RemoveEmptyEntries))
                            {
                                var parts = pair.Split(':');
                                if (parts.Length == 2 && float.TryParse(parts[1],
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float vol))
                                    _discord.SetUserVolume(parts[0], vol);
                            }
                            break;
                        case Packets.CLIENTS:
                            OnClientsChanged?.Invoke(System.Text.Encoding.UTF8.GetString(data, 1, data.Length - 1).Split(',', StringSplitOptions.RemoveEmptyEntries));
                            break;
                        case Packets.LEVELS:
                            foreach (var pair in System.Text.Encoding.UTF8.GetString(data, 1, data.Length - 1).Split(',', StringSplitOptions.RemoveEmptyEntries))
                            { var p = pair.Split(':'); if (p.Length == 2 && int.TryParse(p[1], out int lvl)) OnPlayerLevel?.Invoke(p[0], lvl); }
                            break;
                    }
                }
                catch (SocketException) { }
                catch (Exception ex) { if (!ct.IsCancellationRequested) Log($"Receive error: {ex.Message}"); }
            }
        }

        private void KeepaliveLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            { try { SendPacket(Packets.KEEPALIVE); Task.Delay(1000, ct).Wait(ct); } catch { break; } }
        }

        private void SendPacket(byte type, byte[] payload = null)
        {
            try
            {
                int len = payload?.Length ?? 0;
                byte[] packet = new byte[1 + len]; packet[0] = type;
                if (payload != null && len > 0) Buffer.BlockCopy(payload, 0, packet, 1, len);
                _udp.Send(packet, packet.Length);
            }
            catch { }
        }

        private void Log(string msg) { Console.WriteLine($"[discord-client] {msg}"); SetStatus(msg); }
        private void SetStatus(string s) => OnStatusChanged?.Invoke(s);
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;

namespace LOLProximityVC
{
    /// <summary>
    /// Discord mode server.
    /// Calculates proximity volumes and sends PKT_VOLUMES to each client.
    /// </summary>

    public class DiscordServer
    {
        public int Port { get; set; } = 7777;
        public int DetectionPort { get; set; } = 7778;
        public float FullVolumeDistance { get => _proximity.FullVolumeDistance; set => _proximity.FullVolumeDistance = value; }
        public float ZeroVolumeDistance { get => _proximity.ZeroVolumeDistance; set => _proximity.ZeroVolumeDistance = value; }

        private const int ClientTimeoutSeconds = 10;
        private const int CleanupIntervalMs = 2000;
        private const int VolumesIntervalMs = 200;

        private UdpClient _socket;
        private UdpClient _detectionSocket;
        private CancellationTokenSource _cts;
        private readonly object _clientLock = new();
        private readonly Dictionary<string, DiscordClientEntry> _clients = new();
        private readonly PositionState _positionState;
        private readonly ProximityEngine _proximity;

        public event Action<string> OnLog;
        public event Action<List<string>> OnClientsChanged;
        public event Action<string, int> OnClientLevel;
        public event Action<Dictionary<string, (float X, float Y)>> OnPositionsUpdated;
        public event Action<string, string> OnDiscordIdReceived;

        public DiscordServer() { _positionState = new PositionState(); _proximity = new ProximityEngine(_positionState); }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _socket = new UdpClient(Port);
            _detectionSocket = new UdpClient(DetectionPort);
            Log($"Discord server on UDP 0.0.0.0:{Port} | detection bridge on 127.0.0.1:{DetectionPort}");
            var ct = _cts.Token;
            Task.Run(() => ReceiveLoop(ct));
            Task.Run(() => VolumesLoop(ct));
            Task.Run(() => CleanupLoop(ct));
            Task.Run(() => DetectionLoop(ct));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _socket?.Close();
            _detectionSocket?.Close();
            _positionState.Clear();
            lock (_clientLock) { _clients.Clear(); }
            Log("Discord server stopped.");
            OnClientsChanged?.Invoke(new List<string>());
        }

        private void ReceiveLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    var data = _socket.Receive(ref ep);
                    if (data.Length < 1) continue;
                    byte[] payload = data.Length > 1 ? data[1..] : Array.Empty<byte>();
                    switch (data[0])
                    {
                        case Packets.CONNECT: HandleConnect(ep, payload); break;
                        case Packets.DISCORD_ID: HandleDiscordId(ep, payload); break;
                        case Packets.KEEPALIVE: HandleKeepalive(ep); break;
                        case Packets.DISCONNECT: HandleDisconnect(ep); break;
                        case Packets.AUDIO: break; // ignored in Discord mode
                        default: Log($"Unknown packet {data[0]:#04x} from {ep}"); break;
                    }
                }
                catch (SocketException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) when (!ct.IsCancellationRequested) { Log($"Receive error: {ex.Message}"); }
            }
        }

        private void DetectionLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    var data = _detectionSocket.Receive(ref ep);
                    var root = JsonDocument.Parse(Encoding.UTF8.GetString(data)).RootElement;
                    string champ = root.GetProperty("champion").GetString()?.ToLower();
                    float x = root.GetProperty("x").GetSingle();
                    float y = root.GetProperty("y").GetSingle();
                    if (!string.IsNullOrEmpty(champ)) { _positionState.Update(champ, x, y); OnPositionsUpdated?.Invoke(_positionState.GetAll()); }
                }
                catch (SocketException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) when (!ct.IsCancellationRequested) { Log($"Detection error: {ex.Message}"); }
            }
        }

        private void HandleConnect(IPEndPoint addr, byte[] payload)
        {
            string name;
            try { name = Encoding.UTF8.GetString(payload).Trim().ToLower(); }
            catch { SendTo(addr, Packets.REJECT, Encoding.UTF8.GetBytes("Invalid name")); return; }
            if (string.IsNullOrEmpty(name)) { SendTo(addr, Packets.REJECT, Encoding.UTF8.GetBytes("Empty name")); return; }

            bool reconnect;
            lock (_clientLock)
            {
                reconnect = _clients.ContainsKey(name);
                _clients[name] = new DiscordClientEntry { Address = addr, LastSeen = DateTime.UtcNow };
            }
            Log($"{(reconnect ? "Reconnect" : "Connect")}: '{name}' from {addr}");
            SendTo(addr, Packets.ACK);
            SendTo(addr, Packets.MODE, Encoding.UTF8.GetBytes("discord"));
            BroadcastClientList();
        }

        private void HandleDiscordId(IPEndPoint addr, byte[] payload)
        {
            string id = Encoding.UTF8.GetString(payload).Trim();
            lock (_clientLock)
            {
                foreach (var (name, client) in _clients)
                    if (client.Address.Address.Equals(addr.Address))
                    { client.DiscordId = id; Log($"Discord ID for '{name}': {id}"); OnDiscordIdReceived?.Invoke(name, id); return; }
            }
        }

        private void HandleKeepalive(IPEndPoint addr)
        {
            lock (_clientLock)
            {
                foreach (var c in _clients.Values)
                    if (c.Address.Address.Equals(addr.Address)) { c.LastSeen = DateTime.UtcNow; return; }
            }
        }

        private void HandleDisconnect(IPEndPoint addr)
        {
            string removed = null;
            lock (_clientLock)
            {
                foreach (var kv in _clients)
                    if (kv.Value.Address.Address.Equals(addr.Address)) { removed = kv.Key; break; }
                if (removed != null) _clients.Remove(removed);
            }
            if (removed != null) { Log($"Disconnected: '{removed}'"); BroadcastClientList(); }
        }

        private void VolumesLoop(CancellationToken ct)
        {
            Log("Volumes loop started.");
            while (!ct.IsCancellationRequested)
            {
                Thread.Sleep(VolumesIntervalMs);
                Dictionary<string, DiscordClientEntry> snapshot;
                lock (_clientLock) { snapshot = new Dictionary<string, DiscordClientEntry>(_clients); }
                if (snapshot.Count == 0) continue;

                var connectedNames = new List<string>(snapshot.Keys);
                foreach (var (recipientName, recipient) in snapshot)
                {
                    var volumes = _proximity.GetVolumesFor(recipientName, connectedNames);
                    var parts = new List<string>();
                    foreach (var (senderName, sender) in snapshot)
                    {
                        if (senderName == recipientName || sender.DiscordId == null) continue;
                        float vol = volumes.TryGetValue(senderName, out float v) ? v : 1.0f;
                        parts.Add($"{sender.DiscordId}:{vol.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                    }
                    if (parts.Count > 0)
                        SendTo(recipient.Address, Packets.VOLUMES, Encoding.UTF8.GetBytes(string.Join(",", parts)));
                }
            }
        }

        private void CleanupLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                Thread.Sleep(CleanupIntervalMs);
                var now = DateTime.UtcNow; var stale = new List<string>();
                lock (_clientLock)
                {
                    foreach (var kv in _clients) if ((now - kv.Value.LastSeen).TotalSeconds > ClientTimeoutSeconds) stale.Add(kv.Key);
                    foreach (var name in stale) { Log($"Timed out: '{name}'"); _clients.Remove(name); }
                }
                if (stale.Count > 0) BroadcastClientList();
                List<string> connected; lock (_clientLock) { connected = _clients.Keys.ToList(); }
                var pruned = _positionState.PruneStale(TimeSpan.FromSeconds(5), connected);
                foreach (var name in pruned) Log($"Pruned: '{name}'");
                if (pruned.Count > 0) OnPositionsUpdated?.Invoke(_positionState.GetAll());
                lock (_clientLock) { if (_clients.Count > 0) OnClientsChanged?.Invoke(_clients.Keys.ToList()); }
            }
        }

        private void BroadcastClientList()
        {
            List<string> names; List<IPEndPoint> addrs;
            lock (_clientLock) { names = _clients.Keys.ToList(); addrs = _clients.Values.Select(c => c.Address).ToList(); }
            byte[] payload = Encoding.UTF8.GetBytes(string.Join(",", names));
            foreach (var addr in addrs) SendTo(addr, Packets.CLIENTS, payload);
            Log($"Clients: [{string.Join(", ", names)}]");
            OnClientsChanged?.Invoke(names);
        }

        private void SendTo(IPEndPoint addr, byte type, byte[] payload = null)
        {
            try
            {
                int len = payload?.Length ?? 0; byte[] packet = new byte[1 + len]; packet[0] = type;
                if (payload != null && len > 0) Buffer.BlockCopy(payload, 0, packet, 1, len);
                _socket.Send(packet, packet.Length, addr);
            }
            catch { }
        }

        private void Log(string msg) { Console.WriteLine($"[discord-server] {msg}"); OnLog?.Invoke(msg); }
    }

    public class DiscordClientEntry
    {
        public IPEndPoint Address { get; set; }
        public DateTime LastSeen { get; set; }
        public string DiscordId { get; set; }
    }
}
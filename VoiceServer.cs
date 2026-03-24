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
    /// VOIP mode server.
    /// Work in progress.
    /// Currently semi-functional.
    /// Use Discord mode until further notice.
    /// </summary>

    public class VoiceServer
    {
        public const int SampleRate = 44100;
        public const int SampleWidth = 2;
        public const int ChunkSize = 882;

        public int Port { get; set; } = 7777;
        public int DetectionPort { get; set; } = 7778;
        public float FullVolumeDistance { get => _proximity.FullVolumeDistance; set => _proximity.FullVolumeDistance = value; }
        public float ZeroVolumeDistance { get => _proximity.ZeroVolumeDistance; set => _proximity.ZeroVolumeDistance = value; }

        private const int ClientTimeoutSeconds = 10;
        private const int CleanupIntervalMs = 2000;
        private const int LevelsIntervalMs = 100;

        private UdpClient _voiceSocket;
        private UdpClient _detectionSocket;
        private CancellationTokenSource _cts;
        private readonly object _clientLock = new();
        private readonly Dictionary<string, VoiceClientEntry> _clients = new();
        private readonly PositionState _positionState;
        private readonly ProximityEngine _proximity;

        public event Action<string> OnLog;
        public event Action<List<string>> OnClientsChanged;
        public event Action<string, int> OnClientLevel;
        public event Action<Dictionary<string, (float X, float Y)>> OnPositionsUpdated;

        public VoiceServer() { _positionState = new PositionState(); _proximity = new ProximityEngine(_positionState); }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _voiceSocket = new UdpClient(Port);
            _detectionSocket = new UdpClient(DetectionPort);
            Log($"VOIP server on UDP 0.0.0.0:{Port} | detection bridge on 127.0.0.1:{DetectionPort}");
            var ct = _cts.Token;
            Task.Run(() => ReceiveLoop(ct));
            Task.Run(() => SendLoop(ct));
            Task.Run(() => LevelsLoop(ct));
            Task.Run(() => CleanupLoop(ct));
            Task.Run(() => DetectionLoop(ct));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _voiceSocket?.Close();
            _detectionSocket?.Close();
            _positionState.Clear();
            lock (_clientLock) { _clients.Clear(); }
            Log("VOIP server stopped.");
            OnClientsChanged?.Invoke(new List<string>());
        }

        private void ReceiveLoop(CancellationToken ct)
        {
            int audioCount = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    var data = _voiceSocket.Receive(ref ep);
                    if (data.Length < 1) continue;
                    byte[] payload = data.Length > 1 ? data[1..] : Array.Empty<byte>();
                    switch (data[0])
                    {
                        case Packets.CONNECT: HandleConnect(ep, payload); break;
                        case Packets.AUDIO: HandleAudio(ep, payload); if (++audioCount % 500 == 0) Log($"Received {audioCount} audio packets"); break;
                        case Packets.KEEPALIVE: HandleKeepalive(ep); break;
                        case Packets.DISCONNECT: HandleDisconnect(ep); break;
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
                _clients[name] = new VoiceClientEntry { Address = addr, Audio = new byte[ChunkSize * SampleWidth], LastSeen = DateTime.UtcNow };
            }
            Log($"{(reconnect ? "Reconnect" : "Connect")}: '{name}' from {addr}");
            SendTo(addr, Packets.ACK);
            SendTo(addr, Packets.MODE, Encoding.UTF8.GetBytes("voip"));
            BroadcastClientList();
        }

        private void HandleAudio(IPEndPoint addr, byte[] payload)
        {
            lock (_clientLock)
            {
                foreach (var c in _clients.Values)
                    if (c.Address.Address.Equals(addr.Address))
                    { c.Address = addr; c.Audio = (byte[])payload.Clone(); c.LastSeen = DateTime.UtcNow; return; }
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

        private void SendLoop(CancellationToken ct)
        {
            int sendCount = 0;
            while (!ct.IsCancellationRequested)
            {
                var start = DateTime.UtcNow;
                Dictionary<string, VoiceClientEntry> snapshot;
                lock (_clientLock) { snapshot = new Dictionary<string, VoiceClientEntry>(_clients); }

                foreach (var (recipientName, recipient) in snapshot)
                {
                    var volumes = _proximity.GetVolumesFor(recipientName, new List<string>(snapshot.Keys));
                    var chunksToMix = new List<(byte[] Audio, float Volume)>();
                    foreach (var (senderName, sender) in snapshot)
                    {
                        if (senderName == recipientName) continue;
                        float vol = volumes.TryGetValue(senderName, out float v) ? v : 1.0f;
                        byte[] ac;
                        lock (_clientLock) { ac = sender.Audio != null ? (byte[])sender.Audio.Clone() : null; }
                        if (vol > 0 && ac?.Length > 0) chunksToMix.Add((ac, vol));
                    }
                    if (chunksToMix.Count > 0)
                    {
                        SendTo(recipient.Address, Packets.AUDIO, MixAudio(chunksToMix));
                        if (++sendCount % 500 == 0) Log($"Sent {sendCount} packets to '{recipientName}'");
                    }
                }

                int sleep = (int)(20 - (DateTime.UtcNow - start).TotalMilliseconds);
                if (sleep > 0) Thread.Sleep(sleep);
            }
        }

        private void LevelsLoop(CancellationToken ct)
        {
            int logCounter = 0;
            while (!ct.IsCancellationRequested)
            {
                Thread.Sleep(LevelsIntervalMs);
                Dictionary<string, VoiceClientEntry> snapshot;
                lock (_clientLock) { snapshot = new Dictionary<string, VoiceClientEntry>(_clients); }
                if (snapshot.Count == 0) continue;

                var parts = new List<string>();
                foreach (var (name, client) in snapshot)
                {
                    byte[] ac; lock (_clientLock) { ac = client.Audio != null ? (byte[])client.Audio.Clone() : null; }
                    int level = CalculateLevel(ac);
                    parts.Add($"{name}:{level}");
                    OnClientLevel?.Invoke(name, level);
                }
                byte[] payload = Encoding.UTF8.GetBytes(string.Join(",", parts));
                foreach (var c in snapshot.Values) SendTo(c.Address, Packets.LEVELS, payload);
                if (++logCounter % 50 == 0) Log($"Levels: {string.Join(", ", parts)}");
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

        private byte[] MixAudio(List<(byte[] Audio, float Volume)> chunks)
        {
            float[] mixed = new float[ChunkSize];
            foreach (var (audio, volume) in chunks)
            {
                int available = Math.Min(audio.Length, ChunkSize * SampleWidth);
                for (int i = 0; i < available - 1; i += 2)
                    mixed[i / 2] += BitConverter.ToInt16(audio, i) * volume;
            }
            byte[] output = new byte[ChunkSize * SampleWidth];
            for (int i = 0; i < ChunkSize; i++)
            {
                short c = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, mixed[i]));
                output[i * 2] = (byte)(c & 0xFF); output[i * 2 + 1] = (byte)((c >> 8) & 0xFF);
            }
            return output;
        }

        private int CalculateLevel(byte[] audio)
        {
            if (audio == null || audio.Length < 2) return 0;
            float sum = 0; int samples = audio.Length / 2;
            for (int i = 0; i < audio.Length - 1; i += 2) { short s = BitConverter.ToInt16(audio, i); sum += s * s; }
            return (int)Math.Min(100, (float)Math.Sqrt(sum / samples) / 32767f * 100f * 5f);
        }

        private void SendTo(IPEndPoint addr, byte type, byte[] payload = null)
        {
            try
            {
                int len = payload?.Length ?? 0; byte[] packet = new byte[1 + len]; packet[0] = type;
                if (payload != null && len > 0) Buffer.BlockCopy(payload, 0, packet, 1, len);
                _voiceSocket.Send(packet, packet.Length, addr);
            }
            catch { }
        }

        private void Log(string msg) { Console.WriteLine($"[voip-server] {msg}"); OnLog?.Invoke(msg); }
    }

    public class VoiceClientEntry
    {
        public IPEndPoint Address { get; set; }
        public byte[] Audio { get; set; }
        public DateTime LastSeen { get; set; }
    }
}
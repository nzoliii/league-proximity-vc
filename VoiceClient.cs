using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace LOLProximityVC
{
    /// <summary>
    /// VOIP mode client.
    /// Work in progress.
    /// Currently semi functional.
    /// Use Discord mode until further notice.
    /// </summary>

    public class VoiceClient
    {
        private const int SampleRate = 44100;
        private const int Channels = 1;
        private const int BitsPerSamp = 16;
        private const int ChunkSize = 882;

        private readonly string _server;
        private readonly int _port;
        private readonly string _champion;
        private int _deviceIndex;
        private float _gain;

        private UdpClient _udp;
        private CancellationTokenSource _cts;
        private bool _connected = false;
        private bool _playbackStarted = false;
        private byte[] _micAccumulator = Array.Empty<byte>();

        private WaveInEvent _waveIn;
        private BufferedWaveProvider _playBuffer;
        private WaveOutEvent _waveOut;

        private int _audioSentCount = 0;
        private int _audioReceivedCount = 0;

        public event Action<string> OnStatusChanged;
        public event Action<int, bool> OnMicLevelChanged;
        public event Action<string[]> OnClientsChanged;
        public event Action<string, int> OnPlayerLevel;

        public VoiceClient(string server, int port, string champion, int deviceIndex, float gain)
        {
            _server = server; _port = port; _champion = champion;
            _deviceIndex = deviceIndex; _gain = gain;
        }

        public void Connect()
        {
            _cts = new CancellationTokenSource();
            _udp = new UdpClient();
            _udp.Client.ReceiveTimeout = 2000;
            Console.WriteLine($"[voip-client] Connecting to {_server}:{_port} as '{_champion}'");
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
            _connected = false; _playbackStarted = false;
            StopAudio();
            _udp?.Close();
            Console.WriteLine("[voip-client] Disconnected.");
            SetStatus("Disconnected");
        }

        public void SetGain(float gain) => _gain = gain;

        public void ChangeInputDevice(int deviceIndex)
        {
            _deviceIndex = deviceIndex;
            if (_connected) { _waveIn?.StopRecording(); _waveIn?.Dispose(); StartMic(); }
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
                    Console.WriteLine("[voip-client] ACK received");
                    SetStatus($"Connected as '{_champion}'");
                    StartAudio();
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

        private void StartAudio()
        {
            var format = new WaveFormat(SampleRate, BitsPerSamp, Channels);
            _playBuffer = new BufferedWaveProvider(format) { BufferDuration = TimeSpan.FromMilliseconds(1000), DiscardOnBufferOverflow = true };
            _waveOut = new WaveOutEvent { DesiredLatency = 200, NumberOfBuffers = 4 };
            _waveOut.Init(_playBuffer);
            StartMic();
        }

        private void StartMic()
        {
            var format = new WaveFormat(SampleRate, BitsPerSamp, Channels);
            _waveIn = new WaveInEvent { DeviceNumber = _deviceIndex, WaveFormat = format, BufferMilliseconds = 20 };
            _waveIn.DataAvailable += OnMicData;
            _waveIn.StartRecording();
            Console.WriteLine($"[voip-client] Mic: '{WaveInEvent.GetCapabilities(_deviceIndex).ProductName}'");
        }

        private void StopAudio()
        {
            _waveIn?.StopRecording(); _waveIn?.Dispose();
            _waveOut?.Stop(); _waveOut?.Dispose();
        }

        private void OnMicData(object sender, WaveInEventArgs e)
        {
            if (!_connected) return;
            int needed = ChunkSize * 2;
            _micAccumulator = _micAccumulator.Concat(e.Buffer.Take(e.BytesRecorded)).ToArray();

            while (_micAccumulator.Length >= needed)
            {
                byte[] chunk = _micAccumulator.Take(needed).ToArray();
                _micAccumulator = _micAccumulator.Skip(needed).ToArray();
                byte[] processed = ApplyGain(chunk, needed, _gain);
                SendPacket(Packets.AUDIO, processed, processed.Length);
                if (++_audioSentCount % 500 == 0)
                    Console.WriteLine($"[voip-client] Sent {_audioSentCount} audio packets");

                float sum = 0; int samples = processed.Length / 2;
                for (int i = 0; i < processed.Length - 1; i += 2) { short s = BitConverter.ToInt16(processed, i); sum += s * s; }
                float rms = (float)Math.Sqrt(sum / samples);
                int level = (int)Math.Min(100, rms / 32767f * 100f * 5f);
                OnMicLevelChanged?.Invoke(level, level >= 95);
            }
        }

        private byte[] ApplyGain(byte[] buffer, int length, float gain)
        {
            byte[] output = new byte[length];
            for (int i = 0; i < length - 1; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                short clipped = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, sample * gain));
                output[i] = (byte)(clipped & 0xFF);
                output[i + 1] = (byte)((clipped >> 8) & 0xFF);
            }
            return output;
        }

        private void ReceiveLoop(CancellationToken ct)
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    byte[] data = _udp.Receive(ref ep);
                    if (data.Length < 2) continue;
                    switch (data[0])
                    {
                        case Packets.AUDIO:
                            int audioLen = data.Length - 1;
                            if (audioLen != ChunkSize * 2)
                                Console.WriteLine($"[voip-client] WARNING: chunk size {audioLen} != {ChunkSize * 2}");
                            _playBuffer.AddSamples(data, 1, audioLen);
                            if (++_audioReceivedCount % 100 == 0)
                                Console.WriteLine($"[voip-client] Received {_audioReceivedCount} packets | buffered: {_playBuffer.BufferedBytes}");
                            if (!_playbackStarted && _playBuffer.BufferedBytes >= audioLen * 2)
                            { _waveOut.Play(); _playbackStarted = true; Console.WriteLine("[voip-client] Playback started"); }
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

        private void SendPacket(byte type, byte[] payload = null, int length = -1)
        {
            try
            {
                int len = payload == null ? 0 : (length < 0 ? payload.Length : length);
                byte[] packet = new byte[1 + len]; packet[0] = type;
                if (payload != null && len > 0) Buffer.BlockCopy(payload, 0, packet, 1, len);
                _udp.Send(packet, packet.Length);
            }
            catch { }
        }

        private void Log(string msg) { Console.WriteLine($"[voip-client] {msg}"); SetStatus(msg); }
        private void SetStatus(string s) => OnStatusChanged?.Invoke(s);
    }
}
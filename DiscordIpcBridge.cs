using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace LOLProximityVC
{
    /// <summary>
    /// Connects to Discord's local IPC pipe and sets per-user voice volumes.
    /// Discord allows local volume control via IPC.
    /// </summary>

    public class DiscordIpcBridge : IDisposable
    {
        private NamedPipeClientStream _pipe;
        private readonly object _lock = new();
        private bool _connected = false;
        private int _nonce = 1;

        public event Action<string> OnLog;

        // Connect

        public bool Connect()
        {
            // Discord tries pipe names discord-ipc-0 through discord-ipc-9
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    string pipeName = $"discord-ipc-{i}";
                    _pipe = new NamedPipeClientStream(".", pipeName,
                        PipeDirection.InOut, PipeOptions.Asynchronous);
                    _pipe.Connect(2000);

                    Log($"Connected to Discord IPC pipe: {pipeName}");

                    // Read the initial HELLO frame Discord sends on connect
                    ReadFrame();

                    _connected = true;
                    return true;
                }
                catch
                {
                    _pipe?.Dispose();
                    _pipe = null;
                }
            }

            Log("Could not connect to Discord IPC — is Discord running?");
            return false;
        }

        public void Disconnect()
        {
            _connected = false;
            _pipe?.Dispose();
            _pipe = null;
            Log("Disconnected from Discord IPC.");
        }

        public void Dispose() => Disconnect();

        /// <summary>
        /// Set a user's local Discord volume.
        /// volume: 0.0 (silent) to 2.0 (200%, Discord max)
        /// </summary>
        public bool SetUserVolume(string discordUserId, float volume)
        {
            if (!_connected || _pipe == null) return false;

            // Clamp to Discord's 0–200 range
            int discordVolume = (int)Math.Round(Math.Clamp(volume, 0f, 2f) * 100f);

            var payload = new
            {
                cmd = "SET_USER_VOICE_SETTINGS",
                args = new { user_id = discordUserId, volume = discordVolume },
                nonce = (_nonce++).ToString()
            };

            return SendFrame(payload);
        }

        /// <summary>
        /// Reset all given user IDs to 100% volume (normal).
        /// Call this on shutdown.
        /// </summary>
        public void ResetVolumes(System.Collections.Generic.IEnumerable<string> userIds)
        {
            foreach (var id in userIds)
                SetUserVolume(id, 1.0f);
        }

        // IPC frame protocol
        // Discord IPC uses a simple framing protocol:
        // 4 bytes opcode (little-endian) + 4 bytes length (little-endian) + JSON payload

        private bool SendFrame(object payload)
        {
            try
            {
                string json = JsonSerializer.Serialize(payload);
                byte[] body = Encoding.UTF8.GetBytes(json);
                byte[] opcode = BitConverter.GetBytes(1);     // Opcode 1 = FRAME
                byte[] length = BitConverter.GetBytes(body.Length);

                lock (_lock)
                {
                    _pipe.Write(opcode, 0, 4);
                    _pipe.Write(length, 0, 4);
                    _pipe.Write(body, 0, body.Length);
                    _pipe.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"IPC send error: {ex.Message}");
                _connected = false;
                return false;
            }
        }

        private string ReadFrame()
        {
            try
            {
                byte[] header = new byte[8];
                int read = 0;
                while (read < 8)
                    read += _pipe.Read(header, read, 8 - read);

                int length = BitConverter.ToInt32(header, 4);
                if (length <= 0) return string.Empty;

                byte[] body = new byte[length];
                read = 0;
                while (read < length)
                    read += _pipe.Read(body, read, length - read);

                return Encoding.UTF8.GetString(body);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void Log(string msg)
        {
            Console.WriteLine($"[discord-ipc] {msg}");
            OnLog?.Invoke(msg);
        }
    }
}
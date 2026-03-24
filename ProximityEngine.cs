using System;
using System.Collections.Generic;

namespace LOLProximityVC
{
    /// <summary>
    /// Calculates per-recipient volume levels based on in-game positions.
    /// Only considers champions that are in the connected clients list.
    /// Detected champions not connected to the server are ignored entirely.
    /// </summary>

    public class ProximityEngine
    {
        private readonly PositionState _state;

        public float FullVolumeDistance { get; set; } = 5000f;
        public float ZeroVolumeDistance { get; set; } = 10000f;

        public ProximityEngine(PositionState state)
        {
            _state = state;
        }

        /// <summary>
        /// Returns { senderName: volume (0.0–1.0) } from the recipient's perspective.
        /// Only senders present in connectedClients are included.
        /// Defaults to 1.0 if positions are not yet known.
        /// </summary>

        public Dictionary<string, float> GetVolumesFor(string recipientName, IEnumerable<string> connectedClients)
        {
            var positions = _state.GetAll();
            var recipientPos = _state.Get(recipientName);
            var volumes = new Dictionary<string, float>();

            foreach (var clientName in connectedClients)
            {
                if (clientName == recipientName) continue;

                // No position yet for this client, default to full volume
                if (!positions.TryGetValue(clientName, out var pos))
                {
                    volumes[clientName] = 1.0f;
                    continue;
                }

                // Recipient position unknown, default to full volume
                if (recipientPos == null)
                {
                    volumes[clientName] = 1.0f;
                    continue;
                }

                float dist = Distance(recipientPos.Value, pos);
                volumes[clientName] = CalculateVolume(dist);
            }

            return volumes;
        }

        private float CalculateVolume(float distance)
        {
            if (distance <= FullVolumeDistance) return 1.0f;
            if (distance >= ZeroVolumeDistance) return 0.0f;
            float span = ZeroVolumeDistance - FullVolumeDistance;
            return 1.0f - (distance - FullVolumeDistance) / span;
        }

        private static float Distance((float X, float Y) a, (float X, float Y) b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
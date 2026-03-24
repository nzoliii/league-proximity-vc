using System;
using System.Collections.Generic;
using System.Linq;

namespace LOLProximityVC
{
    /// <summary>
    /// Thread-safe store for champion positions received from detection.py.
    /// Each entry carries a timestamp so stale positions can be pruned.
    /// </summary>
    public class PositionState
    {
        private readonly Dictionary<string, PositionEntry> _positions = new();
        private readonly object _lock = new();

        public void Update(string championName, float gameX, float gameY)
        {
            lock (_lock)
            {
                _positions[championName] = new PositionEntry
                {
                    X = gameX,
                    Y = gameY,
                    LastSeen = DateTime.UtcNow
                };
            }
        }

        public (float X, float Y)? Get(string championName)
        {
            lock (_lock)
            {
                if (_positions.TryGetValue(championName, out var entry))
                    return (entry.X, entry.Y);
                return null;
            }
        }

        public Dictionary<string, (float X, float Y)> GetAll()
        {
            lock (_lock)
            {
                return _positions.ToDictionary(
                    kv => kv.Key,
                    kv => (kv.Value.X, kv.Value.Y)
                );
            }
        }

        /// <summary>
        /// Remove champions that haven't been seen for longer than maxAge,
        /// unless they are in the protected list (connected clients).
        /// </summary>
        public List<string> PruneStale(TimeSpan maxAge, IEnumerable<string> protectedNames)
        {
            var protected_ = new HashSet<string>(protectedNames);
            var removed = new List<string>();
            var cutoff = DateTime.UtcNow - maxAge;

            lock (_lock)
            {
                foreach (var name in _positions.Keys.ToList())
                {
                    if (protected_.Contains(name)) continue;
                    if (_positions[name].LastSeen < cutoff)
                    {
                        _positions.Remove(name);
                        removed.Add(name);
                    }
                }
            }

            return removed;
        }

        public void Clear()
        {
            lock (_lock) { _positions.Clear(); }
        }

        private class PositionEntry
        {
            public float X { get; set; }
            public float Y { get; set; }
            public DateTime LastSeen { get; set; }
        }
    }
}
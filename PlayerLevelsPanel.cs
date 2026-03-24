using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LOLProximityVC
{
    /// <summary>
    /// Custom panel that draws a list of connected players,
    /// each with a live audio level bar on the right.
    /// </summary>
    public class PlayerLevelsPanel : Panel
    {
        private readonly Dictionary<string, int> _levels = new();
        private readonly object _lock = new();

        private const int RowHeight = 24;
        private const int NameWidth = 120;
        private const int BarPadding = 8;

        public PlayerLevelsPanel()
        {
            DoubleBuffered = true;
            BackColor = SystemColors.Window;
        }

        public void UpdatePlayers(string[] names)
        {
            lock (_lock)
            {
                // Add new players
                foreach (var name in names)
                    if (!_levels.ContainsKey(name))
                        _levels[name] = 0;

                // Remove disconnected players
                var toRemove = new List<string>();
                foreach (var key in _levels.Keys)
                {
                    bool found = false;
                    foreach (var n in names) if (n == key) { found = true; break; }
                    if (!found) toRemove.Add(key);
                }
                foreach (var key in toRemove)
                    _levels.Remove(key);
            }
            Invalidate();
        }

        public void UpdateLevel(string champion, int level)
        {
            lock (_lock)
            {
                if (_levels.ContainsKey(champion))
                    _levels[champion] = level;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            Dictionary<string, int> snapshot;
            lock (_lock) { snapshot = new Dictionary<string, int>(_levels); }

            int y = 4;
            foreach (var kv in snapshot)
            {
                string name = kv.Key;
                int level = kv.Value; // 0–100

                // Name
                g.DrawString(name, Font, Brushes.Black,
                    new RectangleF(4, y + 4, NameWidth, RowHeight),
                    new StringFormat { LineAlignment = StringAlignment.Center });

                // Bar background
                int barX = NameWidth + BarPadding;
                int barW = Width - barX - 8;
                int barH = 12;
                int barY = y + (RowHeight - barH) / 2;

                g.FillRectangle(Brushes.LightGray, barX, barY, barW, barH);

                // Bar fill, green normally, yellow at 70+, red at 90+
                int fillW = (int)(barW * level / 100f);
                if (fillW > 0)
                {
                    Brush barBrush = level >= 90 ? Brushes.Red
                                   : level >= 70 ? Brushes.Orange
                                   : Brushes.MediumSeaGreen;
                    g.FillRectangle(barBrush, barX, barY, fillW, barH);
                }

                // Border
                g.DrawRectangle(Pens.Gray, barX, barY, barW, barH);

                // Separator line
                g.DrawLine(Pens.LightGray, 0, y + RowHeight - 1, Width, y + RowHeight - 1);

                y += RowHeight;
            }

            // Empty state
            if (snapshot.Count == 0)
            {
                g.DrawString("No players connected", Font, Brushes.Gray,
                    new RectangleF(4, 4, Width - 8, Height),
                    new StringFormat { LineAlignment = StringAlignment.Center });
            }
        }
    }
}
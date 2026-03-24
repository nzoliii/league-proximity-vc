using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LOLProximityVC
{
    /// <summary>
    /// Draws a top-down minimap view showing all detected champion positions,
    /// their full-volume radius, and their zero-volume (fade) radius.
    /// </summary>
    public class MinimapPanel : Panel
    {
        private readonly object _lock = new();

        // { championName: (normX 0-1, normY 0-1) }
        private Dictionary<string, (float X, float Y)> _positions = new();

        // Connected client names (drawn differently from unconnected detections)
        private HashSet<string> _connectedClients = new();

        public float FullVolumeDistance { get; set; } = 5000f;
        public float ZeroVolumeDistance { get; set; } = 10000f;
        public float MapWidth { get; set; } = 14870f;
        public float MapHeight { get; set; } = 14870f;

        // Colors
        private static readonly Color ColorConnected = Color.FromArgb(255, 80, 220, 120);
        private static readonly Color ColorDetected = Color.FromArgb(255, 180, 180, 180);
        private static readonly Color ColorFullRadius = Color.FromArgb(40, 80, 220, 120);
        private static readonly Color ColorFadeRadius = Color.FromArgb(15, 80, 180, 255);
        private static readonly Color ColorMapBg = Color.FromArgb(255, 20, 28, 36);
        private static readonly Color ColorMapGrid = Color.FromArgb(40, 255, 255, 255);
        private static readonly Color ColorMapBorder = Color.FromArgb(180, 100, 120, 140);

        public MinimapPanel()
        {
            DoubleBuffered = true;
            BackColor = ColorMapBg;
            MinimumSize = new Size(200, 200);
        }

        public void UpdatePositions(Dictionary<string, (float X, float Y)> positions)
        {
            lock (_lock) { _positions = new Dictionary<string, (float, float)>(positions); }
            Invalidate();
        }

        public void UpdateConnectedClients(IEnumerable<string> clients)
        {
            lock (_lock) { _connectedClients = new HashSet<string>(clients); }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int pw = Width - 2;
            int ph = Height - 2;
            int ox = 1, oy = 1;

            // Background
            g.FillRectangle(new SolidBrush(ColorMapBg), ox, oy, pw, ph);

            // Grid lines
            DrawGrid(g, ox, oy, pw, ph);

            // Border
            g.DrawRectangle(new Pen(ColorMapBorder, 1.5f), ox, oy, pw, ph);

            Dictionary<string, (float X, float Y)> positions;
            HashSet<string> connected;
            lock (_lock)
            {
                positions = new Dictionary<string, (float, float)>(_positions);
                connected = new HashSet<string>(_connectedClients);
            }

            // Draw radii first (behind champion dots)
            foreach (var kv in positions)
            {
                bool isConnected = connected.Contains(kv.Key);
                if (!isConnected) continue; // Only draw radius for connected clients

                var (px, py) = GameToPanel(kv.Value.X, kv.Value.Y, ox, oy, pw, ph);

                // Fade radius (zero volume)
                float zeroR = GameDistToPanel(ZeroVolumeDistance, pw, ph);
                using var fadeBrush = new SolidBrush(ColorFadeRadius);
                g.FillEllipse(fadeBrush,
                    px - zeroR, py - zeroR, zeroR * 2, zeroR * 2);

                // Full volume radius
                float fullR = GameDistToPanel(FullVolumeDistance, pw, ph);
                using var fullBrush = new SolidBrush(ColorFullRadius);
                g.FillEllipse(fullBrush,
                    px - fullR, py - fullR, fullR * 2, fullR * 2);

                // Radius border
                using var radiusPen = new Pen(Color.FromArgb(80, 80, 220, 120), 1f);
                g.DrawEllipse(radiusPen,
                    px - fullR, py - fullR, fullR * 2, fullR * 2);
                using var fadePen = new Pen(Color.FromArgb(50, 80, 180, 255), 1f) { DashStyle = DashStyle.Dash };
                g.DrawEllipse(fadePen,
                    px - zeroR, py - zeroR, zeroR * 2, zeroR * 2);
            }

            // Draw champion dots and labels on top
            foreach (var kv in positions)
            {
                bool isConnected = connected.Contains(kv.Key);
                var (px, py) = GameToPanel(kv.Value.X, kv.Value.Y, ox, oy, pw, ph);

                Color dotColor = isConnected ? ColorConnected : ColorDetected;
                float dotR = isConnected ? 6f : 4f;

                // Dot
                using var dotBrush = new SolidBrush(dotColor);
                g.FillEllipse(dotBrush, px - dotR, py - dotR, dotR * 2, dotR * 2);

                // Dot border
                using var dotPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1f);
                g.DrawEllipse(dotPen, px - dotR, py - dotR, dotR * 2, dotR * 2);

                // Label
                string label = isConnected ? kv.Key : $"{kv.Key} (ignored)";
                var font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
                var textSize = g.MeasureString(label, font);
                float tx = px - textSize.Width / 2;
                float ty = py + dotR + 2;

                // Text shadow
                using var shadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
                g.DrawString(label, font, shadowBrush, tx + 1, ty + 1);

                // Text
                using var textBrush = new SolidBrush(dotColor);
                g.DrawString(label, font, textBrush, tx, ty);
            }

            // Legend
            DrawLegend(g, ox + pw - 130, oy + 6);

            // Empty state
            if (positions.Count == 0)
            {
                var font = new Font("Segoe UI", 9f);
                string msg = "No champions detected";
                var sz = g.MeasureString(msg, font);
                g.DrawString(msg, font,
                    new SolidBrush(Color.FromArgb(80, 255, 255, 255)),
                    ox + (pw - sz.Width) / 2,
                    oy + (ph - sz.Height) / 2);
            }
        }

        private void DrawGrid(Graphics g, int ox, int oy, int pw, int ph)
        {
            using var gridPen = new Pen(ColorMapGrid, 0.5f);
            int lines = 4;
            for (int i = 1; i < lines; i++)
            {
                float x = ox + pw * i / lines;
                float y = oy + ph * i / lines;
                g.DrawLine(gridPen, x, oy, x, oy + ph);
                g.DrawLine(gridPen, ox, y, ox + pw, y);
            }
        }

        private void DrawLegend(Graphics g, float x, float y)
        {
            var font = new Font("Segoe UI", 7f);
            float lineH = 14f;

            DrawLegendItem(g, font, x, y, ColorConnected, "Connected");
            DrawLegendItem(g, font, x, y + lineH, ColorDetected, "Detected");
            DrawLegendItem(g, font, x, y + lineH * 2, Color.FromArgb(120, 80, 220, 120), "Full vol.");
            DrawLegendItem(g, font, x, y + lineH * 3, Color.FromArgb(80, 80, 180, 255), "Fade zone");
        }

        private void DrawLegendItem(Graphics g, Font font, float x, float y, Color color, string label)
        {
            g.FillEllipse(new SolidBrush(color), x, y + 3, 8, 8);
            g.DrawString(label, font, new SolidBrush(Color.FromArgb(180, 255, 255, 255)), x + 12, y);
        }

        // Coordinate helpers

        private (float px, float py) GameToPanel(float gameX, float gameY, int ox, int oy, int pw, int ph)
        {
            // League (0,0) = bottom-left, panel (0,0) = top-left so flip Y
            float nx = gameX / MapWidth;
            float ny = 1f - (gameY / MapHeight);
            return (ox + nx * pw, oy + ny * ph);
        }

        private float GameDistToPanel(float gameDist, int pw, int ph)
        {
            // Use the smaller panel dimension to keep circles round
            float scale = Math.Min(pw / MapWidth, ph / MapHeight);
            return gameDist * scale;
        }
    }
}
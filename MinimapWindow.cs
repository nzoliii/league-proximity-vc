using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LOLProximityVC
{
    public class MinimapWindow : Form
    {
        public MinimapPanel Panel { get; }

        public event Action OnClosed;

        public MinimapWindow()
        {
            Text = "LOLProximityVC - Live Map";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(420, 440);
            MinimumSize = new Size(280, 280);
            BackColor = Color.FromArgb(20, 28, 36);

            Panel = new MinimapPanel { Dock = DockStyle.Fill };
            Controls.Add(Panel);

            Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Right - Width - 20,
                Screen.PrimaryScreen.WorkingArea.Top + 40
            );
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                OnClosed?.Invoke();
            }
            else
            {
                base.OnFormClosing(e);
            }
        }
    }
}
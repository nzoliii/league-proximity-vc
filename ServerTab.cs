using LOLProximityVC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LOLProximityVC
{
    public class ServerTab : UserControl
    {
        private VoiceServer _voiceServer;
        private DiscordServer _discordServer;
        private Process _detectionProcess;
        private MinimapWindow _minimapWindow = new MinimapWindow();

        public event Action OnSessionStarted;
        public event Action OnSessionStopped;

        private Label lblPort;
        private TextBox txtPort;
        private Label lblMode;
        private RadioButton rbVoip;
        private RadioButton rbDiscord;
        private Label lblDetection;
        private TextBox txtDetectionPath;
        private Button btnBrowse;
        private Label lblModel;
        private TextBox txtModelPath;
        private Button btnBrowseModel;
        private Label lblFps;
        private TextBox txtFps;
        private Label lblCountdown;
        private TextBox txtCountdown;
        private Label lblConfidence;
        private TextBox txtConfidence;
        private Label lblMapWidth;
        private TextBox txtMapWidth;
        private Label lblMapHeight;
        private TextBox txtMapHeight;
        private Label lblBridgePort;
        private TextBox txtBridgePort;
        private Label lblFullDist;
        private TrackBar trkFullDist;
        private TextBox txtFullDist;
        private Label lblZeroDist;
        private TrackBar trkZeroDist;
        private TextBox txtZeroDist;
        private CheckBox chkMinimap;
        private CheckBox chkConsole;
        private bool _syncingConsole = false;
        private Button btnStart;
        private Button btnStop;
        private Label lblStatus;

        private Label lblPlayers;
        private PlayerLevelsPanel pnlPlayers = new PlayerLevelsPanel();
        private Label lblDiscordIds;
        private DataGridView dgvDiscordIds;
        private Label lblLog;
        private RichTextBox rtbLog;

        private Label secVoiceServer;
        private Label secDetection;
        private Label secProximity;

        private const int LeftW = 310;
        private const int RightX = 380;
        private const int RightW = 300;
        private const int FormW = 700;

        public ServerTab()
        {
            BuildUI();
            UpdateModeUI();

            _minimapWindow.OnClosed += () =>
            {
                if (InvokeRequired) Invoke(new Action(() => chkMinimap.Checked = false));
                else chkMinimap.Checked = false;
            };
        }

        private void BuildUI()
        {
            int lx = 10, rx = 150;

            secVoiceServer = SL("Voice server", lx, 0);
            lblMode = L("Mode:", lx, 0);
            rbVoip = new RadioButton { Text = "Custom VOIP", Size = new Size(110, 22), Checked = true };
            rbDiscord = new RadioButton { Text = "Discord", Size = new Size(80, 22) };
            rbVoip.CheckedChanged += (s, e) => { if (rbVoip.Checked) UpdateModeUI(); };
            rbDiscord.CheckedChanged += (s, e) => { if (rbDiscord.Checked) UpdateModeUI(); };

            lblPort = L("Port:", lx, 0); txtPort = T(rx, 0, 80); txtPort.Text = AppConfig.Port.ToString();
            lblBridgePort = L("Bridge port:", lx, 0); txtBridgePort = T(rx, 0, 80); txtBridgePort.Text = "7778";

            secDetection = SL("Detection", lx, 0);
            lblDetection = L("detection.py:", lx, 0); txtDetectionPath = T(rx, 0, 130); txtDetectionPath.Text = "detection.py";
            btnBrowse = Btn("...", 0, 0); btnBrowse.Click += (s, e) => Browse(txtDetectionPath, "Python files (*.py)|*.py");
            lblModel = L("Model (.pt):", lx, 0); txtModelPath = T(rx, 0, 130); txtModelPath.Text = "model.pt";
            btnBrowseModel = Btn("...", 0, 0); btnBrowseModel.Click += (s, e) => Browse(txtModelPath, "Model files (*.pt)|*.pt");
            lblFps = L("FPS:", lx, 0); txtFps = T(rx, 0, 60); txtFps.Text = "30";
            lblCountdown = L("Countdown (s):", lx, 0); txtCountdown = T(rx, 0, 60); txtCountdown.Text = "5";
            lblConfidence = L("Confidence:", lx, 0); txtConfidence = T(rx, 0, 60); txtConfidence.Text = "0.5";
            lblMapWidth = L("Map width:", lx, 0); txtMapWidth = T(rx, 0, 80); txtMapWidth.Text = "14870";
            lblMapHeight = L("Map height:", lx, 0); txtMapHeight = T(rx, 0, 80); txtMapHeight.Text = "14870";

            secProximity = SL("Proximity", lx, 0);
            lblFullDist = L("Full volume dist:", lx, 0);
            trkFullDist = new TrackBar { Size = new Size(140, 35), Minimum = 0, Maximum = 14870, Value = 1000, TickFrequency = 1000 };
            txtFullDist = new TextBox { Size = new Size(62, 23), Text = "1000" };
            trkFullDist.ValueChanged += (s, e) =>
            {
                txtFullDist.Text = trkFullDist.Value.ToString();
                if (_voiceServer != null) _voiceServer.FullVolumeDistance = trkFullDist.Value;
                if (_discordServer != null) _discordServer.FullVolumeDistance = trkFullDist.Value;
                if (_minimapWindow?.Panel != null) _minimapWindow.Panel.FullVolumeDistance = trkFullDist.Value;
            };
            txtFullDist.Leave += (s, e) =>
            {
                if (int.TryParse(txtFullDist.Text, out int v) && v >= 0 && v <= 14870)
                { trkFullDist.Value = v; if (_voiceServer != null) _voiceServer.FullVolumeDistance = v; if (_discordServer != null) _discordServer.FullVolumeDistance = v; }
                else txtFullDist.Text = trkFullDist.Value.ToString();
            };

            lblZeroDist = L("Zero volume dist:", lx, 0);
            trkZeroDist = new TrackBar { Size = new Size(140, 35), Minimum = 0, Maximum = 14870, Value = 2000, TickFrequency = 1000 };
            txtZeroDist = new TextBox { Size = new Size(62, 23), Text = "2000" };
            trkZeroDist.ValueChanged += (s, e) =>
            {
                txtZeroDist.Text = trkZeroDist.Value.ToString();
                if (_voiceServer != null) _voiceServer.ZeroVolumeDistance = trkZeroDist.Value;
                if (_discordServer != null) _discordServer.ZeroVolumeDistance = trkZeroDist.Value;
                if (_minimapWindow?.Panel != null) _minimapWindow.Panel.ZeroVolumeDistance = trkZeroDist.Value;
            };
            txtZeroDist.Leave += (s, e) =>
            {
                if (int.TryParse(txtZeroDist.Text, out int v) && v >= 0 && v <= 14870)
                { trkZeroDist.Value = v; if (_voiceServer != null) _voiceServer.ZeroVolumeDistance = v; if (_discordServer != null) _discordServer.ZeroVolumeDistance = v; }
                else txtZeroDist.Text = trkZeroDist.Value.ToString();
            };

            chkMinimap = new CheckBox { Text = "Show live map window", Size = new Size(220, 22), ForeColor = Color.DimGray };
            chkConsole = new CheckBox { Text = "Show console log", Size = new Size(160, 22), ForeColor = Color.DimGray, Checked = false };
            chkConsole.CheckedChanged += (s, e) =>
            {
                if (chkConsole.Checked) Program.ShowConsole();
                else Program.HideConsole();
            };
            chkMinimap.CheckedChanged += (s, e) => { if (chkMinimap.Checked) { _minimapWindow.Show(); _minimapWindow.BringToFront(); } else _minimapWindow.Hide(); };

            btnStart = new Button { Text = "Start server", Size = new Size(110, 28), BackColor = Color.MediumSeaGreen, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnStart.Click += BtnStart_Click;
            btnStop = new Button { Text = "Stop server", Size = new Size(110, 28), Enabled = false, BackColor = Color.IndianRed, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnStop.Click += BtnStop_Click;

            lblStatus = new Label { Text = "Stopped", Size = new Size(LeftW, 20), ForeColor = Color.Gray };

            // Right column, fixed positions
            lblPlayers = new Label { Text = "Connected players:", Location = new Point(RightX, 10), Size = new Size(RightW, 18), ForeColor = Color.DimGray };
            pnlPlayers.Location = new Point(RightX, 30); pnlPlayers.Size = new Size(RightW, 240); pnlPlayers.BorderStyle = BorderStyle.FixedSingle;

            lblDiscordIds = new Label { Text = "Champion → Discord user ID:", Location = new Point(RightX, 280), Size = new Size(RightW, 18), ForeColor = Color.DimGray };
            dgvDiscordIds = new DataGridView
            {
                Location = new Point(RightX, 300),
                Size = new Size(RightW, 240),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.Fixed3D
            };
            dgvDiscordIds.Columns.Add(new DataGridViewTextBoxColumn { Name = "Champion", HeaderText = "Champion", ReadOnly = true, FillWeight = 40 });
            dgvDiscordIds.Columns.Add(new DataGridViewTextBoxColumn { Name = "DiscordId", HeaderText = "Discord user ID", FillWeight = 60 });
            dgvDiscordIds.CellEndEdit += DgvDiscordIds_CellEndEdit;

            lblLog = new Label { Text = "Server log:", Size = new Size(FormW - 20, 18), ForeColor = Color.DimGray };
            rtbLog = new RichTextBox { Size = new Size(FormW - 20, 200), ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LimeGreen, Font = new Font("Consolas", 8f), ScrollBars = RichTextBoxScrollBars.Vertical };

            Controls.AddRange(new Control[] {
                secVoiceServer, lblMode, rbVoip, rbDiscord,
                lblPort, txtPort, lblBridgePort, txtBridgePort,
                secDetection, lblDetection, txtDetectionPath, btnBrowse,
                lblModel, txtModelPath, btnBrowseModel,
                lblFps, txtFps, lblCountdown, txtCountdown, lblConfidence, txtConfidence,
                lblMapWidth, txtMapWidth, lblMapHeight, txtMapHeight,
                secProximity, lblFullDist, trkFullDist, txtFullDist,
                lblZeroDist, trkZeroDist, txtZeroDist,
                chkMinimap, chkConsole, btnStart, btnStop, lblStatus,
                lblPlayers, pnlPlayers, lblDiscordIds, dgvDiscordIds,
                lblLog, rtbLog
            });
        }

        private void UpdateModeUI()
        {
            bool isVoip = rbVoip.Checked;
            lblPort.Visible = isVoip; txtPort.Visible = isVoip;
            lblBridgePort.Visible = isVoip; txtBridgePort.Visible = isVoip;
            lblDiscordIds.Visible = !isVoip; dgvDiscordIds.Visible = !isVoip;
            ReflowControls();
        }

        private void ReflowControls()
        {
            int lx = 10, rx = 150, rowH = 30, secH = 24;
            int y = 10;
            void Place(Control c, int x, int cy) => c.Location = new Point(x, cy);

            Place(secVoiceServer, lx, y); y += secH;
            Place(lblMode, lx, y + 3); Place(rbVoip, rx, y); Place(rbDiscord, rx + 118, y); y += rowH;
            if (lblPort.Visible) { Place(lblPort, lx, y + 3); Place(txtPort, rx, y); y += rowH; }
            if (lblBridgePort.Visible) { Place(lblBridgePort, lx, y + 3); Place(txtBridgePort, rx, y); y += rowH; }

            Place(secDetection, lx, y); y += secH;
            Place(lblDetection, lx, y + 3); Place(txtDetectionPath, rx, y); Place(btnBrowse, rx + 136, y - 1); y += rowH;
            Place(lblModel, lx, y + 3); Place(txtModelPath, rx, y); Place(btnBrowseModel, rx + 136, y - 1); y += rowH;
            Place(lblFps, lx, y + 3); Place(txtFps, rx, y); y += rowH;
            Place(lblCountdown, lx, y + 3); Place(txtCountdown, rx, y); y += rowH;
            Place(lblConfidence, lx, y + 3); Place(txtConfidence, rx, y); y += rowH;
            Place(lblMapWidth, lx, y + 3); Place(txtMapWidth, rx, y); y += rowH;
            Place(lblMapHeight, lx, y + 3); Place(txtMapHeight, rx, y); y += rowH;

            Place(secProximity, lx, y); y += secH;
            Place(lblFullDist, lx, y + 3); Place(trkFullDist, rx, y - 4); Place(txtFullDist, rx + 146, y); y += rowH + 20;
            Place(lblZeroDist, lx, y + 3); Place(trkZeroDist, rx, y - 4); Place(txtZeroDist, rx + 146, y); y += rowH + 20;

            Place(chkMinimap, lx, y); y += 28;
            Place(chkConsole, lx, y); y += 30;
            Place(btnStart, lx, y); Place(btnStop, lx + 118, y); y += 36;
            Place(lblStatus, lx, y); y += 30;

            int rightBottom = dgvDiscordIds.Visible
                ? dgvDiscordIds.Location.Y + dgvDiscordIds.Height + 10
                : pnlPlayers.Location.Y + pnlPlayers.Height + 10;
            int totalHeight = Math.Max(y, rightBottom);

            lblLog.Location = new Point(10, totalHeight);
            rtbLog.Location = new Point(10, totalHeight + 20);
            rtbLog.Size = new Size(FormW - 20, 200);
            lblLog.Size = new Size(FormW - 20, 18);
            totalHeight += 20 + rtbLog.Height + 20;

            var form = FindForm();
            if (form != null) form.ClientSize = new Size(FormW, totalHeight);
        }

        public void TriggerReflow() => ReflowControls();
        public void SyncConsoleCheckbox() => chkConsole.Checked = Program.ConsoleVisible;

        // Helpers

        private Label SL(string text, int x, int y) => new Label { Text = text, Location = new Point(x, y), Size = new Size(LeftW, 18), Font = new Font(Font, FontStyle.Bold), ForeColor = Color.DimGray };
        private Label L(string t, int x, int y) => new Label { Text = t, Location = new Point(x, y + 3), Size = new Size(135, 20) };
        private TextBox T(int x, int y, int w) => new TextBox { Location = new Point(x, y), Size = new Size(w, 23) };
        private Button Btn(string t, int x, int y) => new Button { Text = t, Location = new Point(x, y), Size = new Size(30, 25) };

        private void Browse(TextBox target, string filter)
        {
            using var dlg = new OpenFileDialog { Filter = filter };
            if (dlg.ShowDialog() == DialogResult.OK) target.Text = dlg.FileName;
        }

        // Discord ID table

        private void DgvDiscordIds_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != 1) return;
            var row = dgvDiscordIds.Rows[e.RowIndex];
            AppendLog($"Discord ID updated: '{row.Cells["Champion"].Value}' → {row.Cells["DiscordId"].Value?.ToString()?.Trim()}");
        }

        private void UpsertDiscordIdRow(string championName, string discordId)
        {
            if (InvokeRequired) { Invoke(new Action(() => UpsertDiscordIdRow(championName, discordId))); return; }
            foreach (DataGridViewRow row in dgvDiscordIds.Rows)
                if (row.Cells["Champion"].Value?.ToString() == championName) { row.Cells["DiscordId"].Value = discordId; return; }
            dgvDiscordIds.Rows.Add(championName, discordId);
        }

        private void AddClientToTable(string championName)
        {
            if (InvokeRequired) { Invoke(new Action(() => AddClientToTable(championName))); return; }
            foreach (DataGridViewRow row in dgvDiscordIds.Rows)
                if (row.Cells["Champion"].Value?.ToString() == championName) return;
            dgvDiscordIds.Rows.Add(championName, "");
        }

        private void RemoveClientFromTable(List<string> activeClients)
        {
            if (InvokeRequired) { Invoke(new Action(() => RemoveClientFromTable(activeClients))); return; }
            for (int i = dgvDiscordIds.Rows.Count - 1; i >= 0; i--)
            {
                string ch = dgvDiscordIds.Rows[i].Cells["Champion"].Value?.ToString();
                if (!activeClients.Contains(ch)) dgvDiscordIds.Rows.RemoveAt(i);
            }
        }

        // Start, Stop

        private void BtnStart_Click(object sender, EventArgs e)
        {
            string bpText = txtBridgePort.Visible ? txtBridgePort.Text.Trim() : "7778";
            if (!int.TryParse(bpText, out int bridgePort) || bridgePort < 1 || bridgePort > 65535)
            { MessageBox.Show("Please enter a valid bridge port (1-65535).", "LOLProximityVC"); return; }

            if (rbVoip.Checked)
            {
                if (!int.TryParse(txtPort.Text.Trim(), out int port) || port < 1 || port > 65535)
                { MessageBox.Show("Please enter a valid port (1-65535).", "LOLProximityVC"); return; }

                _voiceServer = new VoiceServer { Port = port, DetectionPort = bridgePort };
                _voiceServer.FullVolumeDistance = trkFullDist.Value;
                _voiceServer.ZeroVolumeDistance = trkZeroDist.Value;
                _voiceServer.OnLog += AppendLog;
                _voiceServer.OnClientsChanged += OnClientsChanged;
                _voiceServer.OnClientLevel += OnClientLevel;
                _voiceServer.OnPositionsUpdated += OnPositionsUpdated;
                _voiceServer.Start();
            }
            else
            {
                _discordServer = new DiscordServer { Port = AppConfig.Port, DetectionPort = bridgePort };
                _discordServer.FullVolumeDistance = trkFullDist.Value;
                _discordServer.ZeroVolumeDistance = trkZeroDist.Value;
                _discordServer.OnLog += AppendLog;
                _discordServer.OnClientsChanged += OnClientsChanged;
                _discordServer.OnClientLevel += OnClientLevel;
                _discordServer.OnPositionsUpdated += OnPositionsUpdated;
                _discordServer.OnDiscordIdReceived += (ch, id) => UpsertDiscordIdRow(ch, id);
                _discordServer.Start();
            }

            StartDetection();

            btnStart.Enabled = false; btnStop.Enabled = true;
            rbVoip.Enabled = false; rbDiscord.Enabled = false;
            if (lblPort.Visible) txtPort.Enabled = false;
            if (lblBridgePort.Visible) txtBridgePort.Enabled = false;
            lblStatus.Text = $"Running — {(rbDiscord.Checked ? "Discord" : "Custom VOIP")} mode";
            lblStatus.ForeColor = Color.MediumSeaGreen;
            OnSessionStarted?.Invoke();
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            StopDetection();
            _voiceServer?.Stop(); _voiceServer = null;
            _discordServer?.Stop(); _discordServer = null;

            btnStart.Enabled = true; btnStop.Enabled = false;
            rbVoip.Enabled = true; rbDiscord.Enabled = true;
            txtPort.Enabled = true; txtBridgePort.Enabled = true;
            lblStatus.Text = "Stopped"; lblStatus.ForeColor = Color.Gray;
            pnlPlayers.UpdatePlayers(Array.Empty<string>());
            dgvDiscordIds.Rows.Clear();
            OnSessionStopped?.Invoke();
        }

        // Detection subprocess

        private void StartDetection()
        {
            string scriptPath = txtDetectionPath.Text.Trim();
            if (!File.Exists(scriptPath)) { AppendLog($"WARNING: detection.py not found at '{scriptPath}'"); return; }

            string bp = txtBridgePort.Visible ? txtBridgePort.Text.Trim() : "7778";
            string args = $"\"{scriptPath}\" --model \"{txtModelPath.Text.Trim()}\" --fps {txtFps.Text.Trim()} --countdown {txtCountdown.Text.Trim()} --confidence {txtConfidence.Text.Trim()} --map-width {txtMapWidth.Text.Trim()} --map-height {txtMapHeight.Text.Trim()} --bridge-port {bp}";

            AppendLog($"Starting: python {args}");
            try
            {
                _detectionProcess = new Process { StartInfo = new ProcessStartInfo { FileName = "python", Arguments = args, UseShellExecute = false, CreateNoWindow = false } };
                _detectionProcess.Start();
                AppendLog($"Detection started (PID {_detectionProcess.Id})");
            }
            catch (Exception ex) { AppendLog($"Failed to start detection: {ex.Message}"); }
        }

        private void StopDetection()
        {
            try
            {
                if (_detectionProcess != null && !_detectionProcess.HasExited)
                { _detectionProcess.Kill(); _detectionProcess.Dispose(); _detectionProcess = null; AppendLog("Detection stopped."); }
            }
            catch (Exception ex) { AppendLog($"Error stopping detection: {ex.Message}"); }
        }

        // Event handlers

        private void OnPositionsUpdated(Dictionary<string, (float X, float Y)> positions)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnPositionsUpdated(positions))); return; }
            if (_minimapWindow?.Panel == null) return;
            _minimapWindow.Panel.FullVolumeDistance = trkFullDist.Value;
            _minimapWindow.Panel.ZeroVolumeDistance = trkZeroDist.Value;
            _minimapWindow.Panel.UpdatePositions(positions);
        }

        private void OnClientsChanged(List<string> clients)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnClientsChanged(clients))); return; }
            pnlPlayers?.UpdatePlayers(clients.ToArray());
            _minimapWindow?.Panel?.UpdateConnectedClients(clients);
            foreach (var c in clients) AddClientToTable(c);
            RemoveClientFromTable(clients);
        }

        private void OnClientLevel(string champion, int level)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnClientLevel(champion, level))); return; }
            pnlPlayers?.UpdateLevel(champion, level);
        }

        private void AppendLog(string msg)
        {
            if (InvokeRequired) { Invoke(new Action(() => AppendLog(msg))); return; }
            rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            rtbLog.ScrollToCaret();
        }

        public void Shutdown()
        {
            StopDetection();
            _voiceServer?.Stop();
            _discordServer?.Stop();
            _minimapWindow?.Dispose();
        }
    }
}
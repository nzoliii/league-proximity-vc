using LOLProximityVC;
using NAudio.Wave;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace LOLProximityVC
{
    public class ClientTab : UserControl
    {
        private VoiceClient _voiceClient;
        private DiscordClient _discordClient;
        private int _selectedDeviceIndex = 0;
        private bool _discordMode = false;

        public event Action OnSessionStarted;
        public event Action OnSessionStopped;

        // Left column, always visible
        private Label lblChampion;
        private TextBox txtChampion;
        private Label lblServer;
        private TextBox txtServer;
        private Label lblPort;
        private TextBox txtPort;
        private Button btnConnect;
        private Button btnDisconnect;
        private Label lblStatus;

        // Left column, VOIP only
        private Label lblDevice;
        private ComboBox cboDevice;
        private Label lblGain;
        private TrackBar trkGain;
        private Label lblGainValue;
        private Label lblMicText;
        private ProgressBar pbMicLevel;

        // Left column, Discord only
        private Panel pnlDiscord;
        private Label lblDiscordId;
        private TextBox txtDiscordId;
        private Button btnSendDiscordId;
        private Label lblDiscordStatus;
        private Label lblDiscordIpc;

        // Right column, shown after mode is known
        private CheckBox chkConsole;
        private bool _syncingConsole = false;
        private Label lblPlayers;
        private PlayerLevelsPanel pnlPlayers;

        private const int LeftW = 280;
        private const int RightX = 300;
        private const int RightW = 280;
        private const int FormWNarrow = 320;
        private const int FormWWide = 600;

        public ClientTab()
        {
            BuildUI();
            PopulateDevices();
            ReflowControls();
        }

        private void BuildUI()
        {
            int lx = 10, rx = 130;

            lblChampion = L("Champion:", lx, 0); txtChampion = T(rx, 0, 140); txtChampion.PlaceholderText = "e.g. jinx";
            lblServer = L("Server:", lx, 0); txtServer = T(rx, 0, 140); txtServer.Text = AppConfig.DefaultServer;
            lblPort = L("Port:", lx, 0); txtPort = T(rx, 0, 80); txtPort.Text = AppConfig.Port.ToString();

            btnConnect = new Button { Text = "Connect", Size = new Size(100, 28) };
            btnConnect.Click += BtnConnect_Click;
            btnDisconnect = new Button { Text = "Disconnect", Size = new Size(100, 28), Enabled = false };
            btnDisconnect.Click += (s, e) => Disconnect();

            lblStatus = new Label { Text = "Disconnected", Size = new Size(LeftW, 20), ForeColor = Color.Gray };

            // VOIP only, hidden until mode known
            lblDevice = L("Input device:", lx, 0); lblDevice.Visible = false;
            cboDevice = new ComboBox { Size = new Size(140, 23), DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
            cboDevice.SelectedIndexChanged += (s, e) => { _selectedDeviceIndex = cboDevice.SelectedIndex; _voiceClient?.ChangeInputDevice(_selectedDeviceIndex); };
            lblGain = L("Mic gain:", lx, 0); lblGain.Visible = false;
            trkGain = new TrackBar { Size = new Size(130, 35), Minimum = 1, Maximum = 400, Value = 100, TickFrequency = 50, Visible = false };
            lblGainValue = new Label { Text = "100%", Size = new Size(40, 20), ForeColor = Color.DimGray, Visible = false };
            trkGain.ValueChanged += (s, e) => { lblGainValue.Text = $"{trkGain.Value}%"; _voiceClient?.SetGain(trkGain.Value / 100f); };
            lblMicText = new Label { Text = "Mic level:", Size = new Size(80, 18), ForeColor = Color.DimGray, Visible = false };
            pbMicLevel = new ProgressBar { Size = new Size(LeftW, 18), Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Visible = false };

            // Discord only, hidden until mode known
            pnlDiscord = new Panel { Size = new Size(LeftW, 100), BorderStyle = BorderStyle.FixedSingle, Visible = false };
            lblDiscordId = new Label { Text = "Your Discord user ID:", Location = new Point(6, 8), Size = new Size(160, 20), ForeColor = Color.DimGray };
            txtDiscordId = new TextBox { Location = new Point(6, 28), Size = new Size(190, 23), PlaceholderText = "e.g. 123456789012345678" };
            btnSendDiscordId = new Button { Text = "Submit", Location = new Point(202, 27), Size = new Size(60, 25) };
            btnSendDiscordId.Click += (s, e) =>
            {
                string id = txtDiscordId.Text.Trim();
                if (string.IsNullOrEmpty(id)) { MessageBox.Show("Please enter your Discord user ID.", "LOLProximityVC"); return; }
                _discordClient?.SendDiscordId(id);
                lblDiscordStatus.Text = $"Submitted: {id}";
                lblDiscordStatus.ForeColor = Color.MediumSeaGreen;
                btnSendDiscordId.Enabled = false;
                txtDiscordId.Enabled = false;
            };
            lblDiscordStatus = new Label { Text = "Enter your Discord user ID", Location = new Point(6, 56), Size = new Size(266, 18), ForeColor = Color.Goldenrod, Font = new Font(Font, FontStyle.Italic) };
            lblDiscordIpc = new Label { Text = "Discord IPC: not connected", Location = new Point(6, 76), Size = new Size(266, 18), ForeColor = Color.Gray };
            pnlDiscord.Controls.AddRange(new Control[] { lblDiscordId, txtDiscordId, btnSendDiscordId, lblDiscordStatus, lblDiscordIpc });

            chkConsole = new CheckBox
            {
                Text = "Show console log",
                Location = new Point(lx, 0),
                Size = new Size(160, 22),
                ForeColor = Color.DimGray,
                Checked = false
            };
            chkConsole.CheckedChanged += (s, e) =>
            {
                if (chkConsole.Checked) Program.ShowConsole();
                else Program.HideConsole();
            };

            // Right column, shown after mode known
            lblPlayers = new Label { Text = "Connected players:", Location = new Point(RightX, 10), Size = new Size(RightW, 18), ForeColor = Color.DimGray, Visible = false };
            pnlPlayers = new PlayerLevelsPanel { Location = new Point(RightX, 30), Size = new Size(RightW, 240), BorderStyle = BorderStyle.FixedSingle, Visible = false };

            Controls.AddRange(new Control[] {
                lblChampion, txtChampion, lblServer, txtServer, lblPort, txtPort,
                btnConnect, btnDisconnect, lblStatus,
                lblDevice, cboDevice, lblGain, trkGain, lblGainValue, lblMicText, pbMicLevel,
                pnlDiscord,
                chkConsole,
                lblPlayers, pnlPlayers
            });
        }

        private void ReflowControls()
        {
            int lx = 10, rx = 130, rowH = 32;
            int y = 12;
            void Place(Control c, int x, int cy) => c.Location = new Point(x, cy);

            Place(lblChampion, lx, y + 3); Place(txtChampion, rx, y); y += rowH;
            Place(lblServer, lx, y + 3); Place(txtServer, rx, y); y += rowH;
            Place(lblPort, lx, y + 3); Place(txtPort, rx, y); y += rowH;
            Place(btnConnect, lx, y); Place(btnDisconnect, lx + 108, y); y += 36;
            Place(lblStatus, lx, y); y += 28;
            Place(chkConsole, lx, y); y += 26;

            if (lblDevice.Visible)
            {
                Place(lblDevice, lx, y + 3); Place(cboDevice, rx, y); y += rowH;
                Place(lblGain, lx, y + 3); Place(trkGain, rx, y - 4); Place(lblGainValue, rx + 136, y + 3); y += rowH + 6;
                Place(lblMicText, lx, y); y += 20;
                Place(pbMicLevel, lx, y); y += 26;
            }

            if (pnlDiscord.Visible) { Place(pnlDiscord, lx, y); y += pnlDiscord.Height + 10; }

            int rightBottom = pnlPlayers.Visible ? pnlPlayers.Location.Y + pnlPlayers.Height + 20 : 0;
            int totalHeight = Math.Max(y + 10, rightBottom);
            if (!lblPlayers.Visible) totalHeight = y + lblStatus.Height + 20;

            var form = FindForm();
            if (form != null)
                form.ClientSize = new Size(lblPlayers.Visible ? FormWWide : FormWNarrow, totalHeight);
        }

        private void PopulateDevices()
        {
            cboDevice.Items.Clear();
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                cboDevice.Items.Add(WaveInEvent.GetCapabilities(i).ProductName);
            if (cboDevice.Items.Count > 0) cboDevice.SelectedIndex = 0;
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            string champion = txtChampion.Text.Trim().ToLower();
            string server = txtServer.Text.Trim();
            if (string.IsNullOrEmpty(champion)) { MessageBox.Show("Please enter your champion name.", "LOLProximityVC"); return; }
            if (string.IsNullOrEmpty(server)) { MessageBox.Show("Please enter the server address.", "LOLProximityVC"); return; }
            if (!int.TryParse(txtPort.Text.Trim(), out int port) || port < 1 || port > 65535) { MessageBox.Show("Please enter a valid port (1-65535).", "LOLProximityVC"); return; }

            btnConnect.Enabled = false;
            btnDisconnect.Enabled = true;
            txtChampion.Enabled = false;
            txtServer.Enabled = false;
            txtPort.Enabled = false;
            lblStatus.Text = "Connecting...";
            OnSessionStarted?.Invoke();

            var probe = new ModeProbe(server, port, champion);
            probe.OnModeReceived += mode => OnModeKnown(mode, server, port, champion);
            probe.OnError += msg => { SetStatus(msg); ResetUI(); };
            probe.Probe();
        }

        private void OnModeKnown(string mode, string server, int port, string champion)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnModeKnown(mode, server, port, champion))); return; }

            _discordMode = mode == "discord";

            if (_discordMode)
            {
                _discordClient = new DiscordClient(server, port, champion);
                _discordClient.OnStatusChanged += SetStatus;
                _discordClient.OnClientsChanged += OnClientsChanged;
                _discordClient.OnPlayerLevel += OnPlayerLevel;
                _discordClient.OnDiscordIpcStatus += ok =>
                {
                    if (InvokeRequired) Invoke(new Action(() => UpdateDiscordIpcLabel(ok)));
                    else UpdateDiscordIpcLabel(ok);
                };
                _discordClient.Connect();

                lblDevice.Visible = false; cboDevice.Visible = false;
                lblGain.Visible = false; trkGain.Visible = false; lblGainValue.Visible = false;
                lblMicText.Visible = false; pbMicLevel.Visible = false;
                pnlDiscord.Visible = true;
                btnSendDiscordId.Enabled = true;
                txtDiscordId.Enabled = true;
            }
            else
            {
                _voiceClient = new VoiceClient(server, port, champion, _selectedDeviceIndex, trkGain.Value / 100f);
                _voiceClient.OnStatusChanged += SetStatus;
                _voiceClient.OnMicLevelChanged += OnMicLevelChanged;
                _voiceClient.OnClientsChanged += OnClientsChanged;
                _voiceClient.OnPlayerLevel += OnPlayerLevel;
                _voiceClient.Connect();

                lblDevice.Visible = true; cboDevice.Visible = true;
                lblGain.Visible = true; trkGain.Visible = true; lblGainValue.Visible = true;
                lblMicText.Visible = true; pbMicLevel.Visible = true;
                pnlDiscord.Visible = false;
            }

            lblPlayers.Visible = true;
            pnlPlayers.Visible = true;
            ReflowControls();
        }

        private void UpdateDiscordIpcLabel(bool connected)
        {
            lblDiscordIpc.Text = connected ? "Discord IPC: connected ✓" : "Discord IPC: failed — is Discord running?";
            lblDiscordIpc.ForeColor = connected ? Color.MediumSeaGreen : Color.IndianRed;
        }

        private void Disconnect()
        {
            _voiceClient?.Disconnect();
            _discordClient?.Disconnect();
            ResetUI();
        }

        private void SetStatus(string status)
        {
            if (InvokeRequired) { Invoke(new Action(() => SetStatus(status))); return; }
            lblStatus.Text = status;
        }

        private void OnMicLevelChanged(int level, bool clipping)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnMicLevelChanged(level, clipping))); return; }
            pbMicLevel.Value = Math.Min(100, Math.Max(0, level));
            pbMicLevel.ForeColor = clipping ? Color.Red : Color.Green;
        }

        private void OnClientsChanged(string[] clients)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnClientsChanged(clients))); return; }
            pnlPlayers.UpdatePlayers(clients);
        }

        private void OnPlayerLevel(string champion, int level)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnPlayerLevel(champion, level))); return; }
            pnlPlayers.UpdateLevel(champion, level);
        }

        private void ResetUI()
        {
            if (InvokeRequired) { Invoke(new Action(ResetUI)); return; }
            _voiceClient = null; _discordClient = null;
            btnConnect.Enabled = true; btnDisconnect.Enabled = false;
            txtChampion.Enabled = true; txtServer.Enabled = true; txtPort.Enabled = true;
            lblStatus.Text = "Disconnected"; lblStatus.ForeColor = Color.Gray;
            pbMicLevel.Value = 0; pbMicLevel.ForeColor = Color.Green;
            lblDevice.Visible = false; cboDevice.Visible = false;
            lblGain.Visible = false; trkGain.Visible = false; lblGainValue.Visible = false;
            lblMicText.Visible = false; pbMicLevel.Visible = false;
            pnlDiscord.Visible = false; txtDiscordId.Text = "";
            lblPlayers.Visible = false; pnlPlayers.Visible = false;
            _discordMode = false;
            pnlPlayers.UpdatePlayers(Array.Empty<string>());
            ReflowControls();
            OnSessionStopped?.Invoke();
        }

        private Label L(string text, int x, int y) => new Label { Text = text, Location = new Point(x, y + 3), Size = new Size(115, 20) };
        private TextBox T(int x, int y, int w) => new TextBox { Location = new Point(x, y), Size = new Size(w, 23) };

        public void TriggerReflow() => ReflowControls();
        public void SyncConsoleCheckbox() => chkConsole.Checked = Program.ConsoleVisible;
        public void Shutdown() { _voiceClient?.Disconnect(); _discordClient?.Disconnect(); }
    }
}
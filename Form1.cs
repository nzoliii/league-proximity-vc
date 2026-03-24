using LOLProximityVC;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace LOLProximityVC
{
    public class Form1 : Form
    {
        private TabControl tabControl;
        private TabPage tabClient;
        private TabPage tabServer;
        private ClientTab clientTab;
        private ServerTab serverTab;

        public Form1()
        {
            Text = "LOLProximityVC";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(320, 300);

            clientTab = new ClientTab { Dock = DockStyle.Fill };
            serverTab = new ServerTab { Dock = DockStyle.Fill };

            clientTab.OnSessionStarted += () => SetTabsLocked(true);
            clientTab.OnSessionStopped += () => SetTabsLocked(false);
            serverTab.OnSessionStarted += () => SetTabsLocked(true);
            serverTab.OnSessionStopped += () => SetTabsLocked(false);

            tabClient = new TabPage("Client");
            tabClient.Controls.Add(clientTab);

            tabServer = new TabPage("Server");
            tabServer.Controls.Add(serverTab);

            tabControl = new TabControl { Dock = DockStyle.Fill };
            tabControl.TabPages.Add(tabClient);
            tabControl.TabPages.Add(tabServer);

            tabControl.Selecting += (s, e) =>
            {
                if (_tabsLocked) e.Cancel = true;
            };

            tabControl.SelectedIndexChanged += (s, e) =>
            {
                // Sync console checkbox on the tab being switched to
                if (tabControl.SelectedIndex == 0)
                {
                    clientTab.SyncConsoleCheckbox();
                    clientTab.TriggerReflow();
                }
                else
                {
                    serverTab.SyncConsoleCheckbox();
                    serverTab.TriggerReflow();
                }
            };

            Controls.Add(tabControl);

            FormClosing += (s, e) =>
            {
                clientTab.Shutdown();
                serverTab.Shutdown();
            };

            // Let the client tab set the initial size after the form loads
            Load += (s, e) => clientTab.TriggerReflow();
        }

        private bool _tabsLocked = false;

        private void SetTabsLocked(bool locked)
        {
            if (InvokeRequired) { Invoke(new Action(() => SetTabsLocked(locked))); return; }
            _tabsLocked = locked;
            int activeIndex = tabControl.SelectedIndex;
            int inactiveIndex = activeIndex == 0 ? 1 : 0;
            tabControl.TabPages[inactiveIndex].Text =
                locked
                    ? (inactiveIndex == 0 ? "Client (locked)" : "Server (locked)")
                    : (inactiveIndex == 0 ? "Client" : "Server");
        }
    }
}
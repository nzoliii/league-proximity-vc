using LOLProximityVC;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LOLProximityVC
{
    internal static class Program
    {
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private static bool _consoleAllocated = false;
        public static bool ConsoleVisible { get; private set; } = false;

        public static void ShowConsole()
        {
            if (!_consoleAllocated)
            {
                AllocConsole();
                Console.Title = "LOLProximityVC — Log";
                _consoleAllocated = true;
            }
            else
            {
                ShowWindow(GetConsoleWindow(), SW_SHOW);
            }
            ConsoleVisible = true;
        }

        public static void HideConsole()
        {
            if (_consoleAllocated)
                ShowWindow(GetConsoleWindow(), SW_HIDE);
            ConsoleVisible = false;
        }

        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}
using System;
using System.Threading;
using System.Windows.Forms;

namespace TempOverlay
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\TempOverlay.SingleInstance";

        [STAThread]
        private static void Main()
        {
            using var mutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out var createdNew);
            if (!createdNew)
            {
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OverlayForm());
        }
    }
}

using Microsoft.Win32;
using System.Diagnostics;

namespace TempOverlay
{
    internal static class StartupManager
    {
        private const string TaskName = "TempOverlay";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "TempOverlay";

        public static void SetStartupEnabled(bool enabled, string exePath)
        {
            DeleteLegacyRunKeyEntry();

            if (enabled)
            {
                CreateOrUpdateStartupTask(exePath);
            }
            else
            {
                DeleteStartupTask();
            }
        }

        private static void DeleteLegacyRunKeyEntry()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
            {
                key?.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }

        private static void CreateOrUpdateStartupTask(string exePath)
        {
            var escapedPath = exePath.Replace("\"", "\"\"");
            var args =
                "/Create " +
                "/TN \"" + TaskName + "\" " +
                "/SC ONLOGON " +
                "/RL HIGHEST " +
                "/F " +
                "/IT " +
                "/TR \"\\\"" + escapedPath + "\\\"\"";

            RunSchtasks(args);
        }

        private static void DeleteStartupTask()
        {
            var args = "/Delete /TN \"" + TaskName + "\" /F";
            RunSchtasks(args);
        }

        private static void RunSchtasks(string args)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    process.Start();
                    process.WaitForExit();
                }
            }
            catch
            {
                // Ignore failures to avoid crashing the UI path.
            }
        }
    }
}

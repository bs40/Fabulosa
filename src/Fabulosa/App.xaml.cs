using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Fabulosa.Model;

namespace Fabulosa
{
    public partial class App : Application
    {
        /// <summary>Command-line options (used for automated verification).</summary>
        public static string ScreenshotPath;
        public static int ScreenshotDelayMs = 2000;
        public static bool ExitAfterScreenshot;
        public static string SelfTestPath;
        public static string WindowShotPath;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ParseArgs(e.Args);
            DispatcherUnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => LogError(a.ExceptionObject as Exception);

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        private static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--screenshot":
                        if (i + 1 < args.Length) { ScreenshotPath = Path.GetFullPath(args[++i]); ExitAfterScreenshot = true; }
                        break;
                    case "--delay":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int ms)) ScreenshotDelayMs = Math.Max(0, ms);
                        break;
                    case "--keep":
                        ExitAfterScreenshot = false;
                        break;
                    case "--selftest":
                        if (i + 1 < args.Length) SelfTestPath = Path.GetFullPath(args[++i]);
                        break;
                    case "--windowshot":
                        if (i + 1 < args.Length) WindowShotPath = Path.GetFullPath(args[++i]);
                        break;
                }
            }
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogError(e.Exception);
            MessageBox.Show("Fabulosa hit an unexpected error:\n\n" + e.Exception.Message + "\n\nDetails were written to " + Path.Combine(Storage.Folder, "error.log"), "Fabulosa", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        public static void LogError(Exception ex)
        {
            if (ex == null) return;
            try { File.AppendAllText(Path.Combine(Storage.Folder, "error.log"), $"[{DateTime.Now:u}] {ex}\n\n"); }
            catch { }
        }
    }
}

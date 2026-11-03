// Closing and starting the Claude desktop app, for the "Restart Claude" button.
// The app only reads its session lists at start, so changes to the signed-in
// account show up after a restart.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SessionSync
{
    static class ClaudeApp
    {
        // The desktop app's processes; the bundled CLI is also claude.exe but lives elsewhere.
        public static List<Process> DesktopProcesses()
        {
            return Process.GetProcessesByName("claude").Where(p => Store.IsDesktopPath(Store.ProcessPath(p))).ToList();
        }

        public static string ExePath(IEnumerable<Process> processes)
        {
            return processes.Select(Store.ProcessPath).FirstOrDefault(p => p != null);
        }

        // Microsoft Store installs live under WindowsApps and start through their app id
        // (Package family name + "!" + application id from AppxManifest.xml). Others start by path.
        public static string AppUserModelId(string exePath)
        {
            if (exePath == null) return null;
            var m = Regex.Match(exePath, @"[\\/]WindowsApps[\\/](([^\\/_]+)_[^\\/]*__([a-z0-9]+))[\\/](.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string packageDir = exePath.Substring(0, exePath.Length - m.Groups[4].Value.Length - 1);
            string relative = m.Groups[4].Value.Replace('/', '\\');
            string appId = null;
            try
            {
                var doc = XDocument.Load(Path.Combine(packageDir, "AppxManifest.xml"));
                foreach (var app in doc.Descendants().Where(e => e.Name.LocalName == "Application"))
                {
                    var exe = (string)app.Attribute("Executable");
                    if (exe != null && exe.Replace('/', '\\').Equals(relative, StringComparison.OrdinalIgnoreCase)) { appId = (string)app.Attribute("Id"); break; }
                }
            }
            catch { }
            return appId == null ? null : m.Groups[2].Value + "_" + m.Groups[3].Value + "!" + appId;
        }

        public static void Launch(string exePath)
        {
            string aumid = AppUserModelId(exePath);
            if (aumid != null) Process.Start("explorer.exe", "shell:AppsFolder\\" + aumid);
            else Process.Start(exePath);
        }

        // Asks every window to close, then waits. False when something is still running
        // (the app may have gone to the tray); the caller can then offer to force it.
        public static bool CloseAll(List<Process> processes, int waitMs)
        {
            foreach (var p in processes)
                try { if (!p.HasExited && p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { }
            var end = DateTime.Now.AddMilliseconds(waitMs);
            while (DateTime.Now < end)
            {
                if (processes.All(Exited)) return true;
                System.Threading.Thread.Sleep(200);
            }
            return processes.All(Exited);
        }

        public static void KillAll(List<Process> processes)
        {
            foreach (var p in processes)
                try { if (!p.HasExited) p.Kill(); } catch { }
            foreach (var p in processes)
                try { p.WaitForExit(5000); } catch { }
        }

        static bool Exited(Process p)
        {
            try { return p.HasExited; } catch { return true; }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

// Without this, a csc-built exe runs with pre-4.6.2 path rules and cannot see
// paths over 260 characters, which Claude's project folders can reach.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace SessionSync
{
    static class Program
    {
        public static void EnableLongPaths()
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // Usage: SessionSync.exe [--demo] [--lang zh|en|ja]
        //   --demo  opens made-up accounts and sessions in a temp folder; your real data is not read.
        [STAThread]
        static int Main(string[] args)
        {
            EnableLongPaths();
            bool demo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
            bool created;
            using (var mutex = new Mutex(true, demo ? @"Local\SessionSync.Demo" : @"Local\SessionSync.SingleInstance", out created))
            {
                if (!created)
                {
                    // Already open: bring that window forward instead of starting a second copy.
                    var me = Process.GetCurrentProcess();
                    foreach (var p in Process.GetProcessesByName(me.ProcessName).Where(p => p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero))
                    {
                        Native.ShowWindow(p.MainWindowHandle, 9);
                        Native.SetForegroundWindow(p.MainWindowHandle);
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                    MessageBox.Show(e.Exception.Message + "\n\n" + e.Exception.StackTrace, L.T("Session 同步器發生錯誤"), MessageBoxButtons.OK, MessageBoxIcon.Error);

                var store = demo ? Demo.Create(Path.Combine(Path.GetTempPath(), "SessionSync-demo")) : Store.FromEnvironment();
                L.Lang = L.Pick(Arg(args, "--lang") ?? store.Pref("lang"));
                Theme.Init();
                if (store.Roots.Count == 0)
                {
                    MessageBox.Show(L.T("找不到 Claude 桌面版的 session 資料夾（claude-code-sessions）。\n\n請確認這台電腦用過 Claude 桌面版的 Code 分頁。"),
                        L.T("Session 同步器"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }

                // Changing the language closes the window and opens a new one here.
                MainForm form;
                do
                {
                    Theme.Init();
                    form = new MainForm(store);
                    Application.Run(form);
                } while (form.Relaunch);
                return 0;
            }
        }
    }
}

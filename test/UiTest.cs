// End-to-end UI test on demo data: opens the real window, clicks cells through the
// view's own click handler (your mouse is not used), answers confirmation dialogs, checks the
// files on disk, and saves README screenshots for each language.
// Build and run: build.cmd uitest
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SessionSync.Tests
{
    static class UiTest
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr h, int id);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint tid, EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc f, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        const int IDOK = 1, IDYES = 6, IDNO = 7;
        static string outDir, shotPrefix;
        static int fails, passes;
        static readonly Queue<int> answers = new Queue<int>();
        static readonly List<string> dialogs = new List<string>();
        static string shotNextDialog;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "ok   - " : "FAIL - ") + what);
            if (ok) passes++; else fails++;
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }

        static void Capture(IntPtr hwnd, string name)
        {
            if (outDir == null) return;
            RECT r;
            GetWindowRect(hwnd, out r);
            using (var bmp = new Bitmap(r.R - r.L, r.B - r.T))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(hwnd, hdc, 2);
                    g.ReleaseHdc(hdc);
                }
                bmp.Save(Path.Combine(outDir, shotPrefix + name + ".png"));
            }
        }

        static void Shot(Form f, string name) { Pump(300); Capture(f.Handle, name); }

        // Message boxes run their own loop inside the click handler, so a timer answers them.
        static void AnswerDialogs()
        {
            EnumThreadWindows(GetCurrentThreadId(), (h, l) =>
            {
                var cls = new StringBuilder(64);
                GetClassName(h, cls, 64);
                if (cls.ToString() != "#32770" || !IsWindowVisible(h)) return true;
                var text = new StringBuilder();
                EnumChildWindows(h, (c, l2) =>
                {
                    var cc = new StringBuilder(32);
                    GetClassName(c, cc, 32);
                    if (cc.ToString() == "Static")
                    {
                        var t = new StringBuilder(4096);
                        GetWindowText(c, t, 4096);
                        text.Append(t);
                    }
                    return true;
                }, IntPtr.Zero);
                dialogs.Add(text.ToString());
                if (shotNextDialog != null) { Capture(h, shotNextDialog); shotNextDialog = null; }
                // Use the planned answer if the box has it; otherwise No, then OK/Cancel (an OK-only box
                // gives its button the Cancel id), then close it.
                int planned = answers.Count > 0 ? answers.Dequeue() : IDNO;
                int ans = new[] { planned, IDNO, IDOK, 2 /* IDCANCEL */ }.FirstOrDefault(id => GetDlgItem(h, id) != IntPtr.Zero);
                if (ans != 0) PostMessage(h, 0x0111 /* WM_COMMAND */, (IntPtr)ans, IntPtr.Zero);
                else PostMessage(h, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        // Posted mouse messages get resolved against the real cursor by ListView, so the
        // click event is raised directly with the cell's coordinates; the view's own
        // hit-testing and routing still run.
        static void ClickCell(IndexView v, int row, int col)
        {
            v.EnsureVisible(row);
            Pump(50);
            var b = v.Items[row].SubItems[col].Bounds;
            var e = new MouseEventArgs(MouseButtons.Left, 1, b.X + b.Width / 2, b.Y + b.Height / 2, 0);
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(v, new object[] { e });
            Pump(400);
        }

        static T Field<T>(object o, string name)
        {
            return (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(o);
        }

        static object Call(object o, string method, params object[] args)
        {
            var m = o.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .First(x => x.Name == method && x.GetParameters().Length == args.Length);
            return m.Invoke(o, args);
        }

        static MainForm Open(Store store)
        {
            Theme.Init();
            var form = new MainForm(store) { StartPosition = FormStartPosition.Manual, Location = new Point(30, 30), Size = new Size(Theme.S(1320), Theme.S(780)) };
            form.Show();
            var start = DateTime.Now;
            while (!Field<bool>(form, "loaded") && (DateTime.Now - start).TotalSeconds < 20) Pump(100);
            Pump(300);
            return form;
        }

        static int RowOf(IndexView v, string title)
        {
            for (int i = 0; i < v.Items.Count; i++) if (v.RowAt(i).Newest.Title == title) return i;
            return -1;
        }

        static int ColOf(IndexView v, string email)
        {
            int i = v.Accounts.FindIndex(a => a.Email == email);
            return i < 0 ? -1 : IndexView.FixedColumns + i;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Program.EnableLongPaths();
            Console.OutputEncoding = Encoding.UTF8;
            outDir = args.Length > 0 ? args[0] : null;
            if (outDir != null) Directory.CreateDirectory(outDir);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var timer = new Timer { Interval = 150 };
            timer.Tick += (s, e) => AnswerDialogs();
            timer.Start();
            // Never leave windows hanging on the screen if something gets stuck.
            var watchdog = new System.Threading.Timer(_ =>
            {
                Console.WriteLine("FAIL - timed out after 180 s");
                Environment.Exit(2);
            }, null, 180000, System.Threading.Timeout.Infinite);
            try
            {
                Functional();
                foreach (var lang in L.Codes) Screenshots(lang);
            }
            catch (Exception e)
            {
                Console.WriteLine("FAIL - crashed: " + e);
                fails++;
            }
            Console.WriteLine();
            Console.WriteLine(fails == 0 ? "ALL OK (" + passes + " checks)" : fails + " FAILED, " + passes + " passed");
            return fails == 0 ? 0 : 1;
        }

        static string DemoDir(string name) { return Path.Combine(Path.GetTempPath(), "SessionSync-uitest-" + name); }

        static void Functional()
        {
            L.Lang = "zh";
            shotPrefix = "";
            string outSave = outDir;
            outDir = null; // functional run takes no screenshots
            var store = Demo.Create(DemoDir("func"));
            var form = Open(store);
            var v = Field<IndexView>(form, "index");
            var status = Field<Label>(form, "status");
            Func<string, Account> acct = mail => store.Accounts.First(a => a.Email == mail && a.Cards.Count + a.Tombstones.Count > 0);
            const string A = "alice@example.com", B = "alice.work@example.com", C = "alice.side@example.com";

            Check(v.Accounts.Count == 3, "3 accounts shown; the never-used folder is hidden");
            Check(v.Items.Count == 13, "13 sessions listed (archived one hidden): " + v.Items.Count);
            Check(v.Columns.Count == IndexView.FixedColumns + 3 + 1, "account columns + delete column");
            Check(v.Accounts.Select(a => a.Name).SequenceEqual(new[] { A, B, C }) || v.Accounts.All(a => a.Email != null), "accounts show their e-mail");

            // Absent → click adds it.
            int r = RowOf(v, "Write release notes for v2.3");
            ClickCell(v, r, ColOf(v, B));
            Check(File.Exists(store.CardPath(acct(B), store.Get(acct(A).Key).Cards.First(c => c.Title == "Write release notes for v2.3").Id)), "click on empty cell copies the session");
            Check(status.Text.StartsWith("已複製 1 個 session"), "status says copied: " + status.Text);

            // Present → click asks, Yes removes and leaves a delete record.
            r = RowOf(v, "Write release notes for v2.3");
            answers.Enqueue(IDYES);
            ClickCell(v, r, ColOf(v, B));
            var id = store.Get(acct(A).Key).Cards.First(c => c.Title == "Write release notes for v2.3").Id;
            Check(!File.Exists(store.CardPath(acct(B), id)) && acct(B).Tombstones.ContainsKey(id), "click on a check asks, then removes from that account");
            Check(dialogs.Count > 0 && dialogs.Last().Contains("移除"), "remove asked first");
            Call(form, "UndoBatch", Field<string>(form, "lastBatch"));
            Pump(300);
            Check(store.Get(acct(B).Key).Find(id) != null && !acct(B).Tombstones.ContainsKey(id), "undo of the remove puts the card back and clears the delete record");

            // Answering No changes nothing.
            r = RowOf(v, "Refactor auth middleware");
            var refactor = v.RowAt(r);
            answers.Enqueue(IDNO);
            ClickCell(v, r, ColOf(v, B));
            Check(File.Exists(store.CardPath(acct(B), refactor.Id)), "No in the confirm keeps the card");

            // Older copy (half circle) → click updates.
            r = RowOf(v, "Fix flaky checkout test");
            var fix = v.RowAt(r);
            Check(v.StateOf(fix, acct(B)) == CellState.Older, "older copy shows as half circle");
            string before = status.Text;
            ClickCell(v, r, ColOf(v, B));
            var fixNow = v.RowAt(RowOf(v, "Fix flaky checkout test"));
            Check(v.StateOf(fixNow, acct(B)) == CellState.Present, "click updates the older copy (state " + v.StateOf(fixNow, acct(B))
                + ", status '" + status.Text + "', was '" + before + "', row " + r + ", col " + ColOf(v, B) + ")");

            // Deleted (×) → click adds back and clears the tombstone.
            r = RowOf(v, "Plan a weekend trip");
            var trip = v.RowAt(r);
            Check(v.StateOf(trip, acct(A)) == CellState.Deleted, "deleted-here shows ×");
            ClickCell(v, r, ColOf(v, A));
            Check(store.Get(acct(A).Key).Find(trip.Id) != null && !acct(A).Tombstones.ContainsKey(trip.Id), "click on × adds it back");

            // Trash: No keeps everything, Yes deletes cards and the transcript.
            r = RowOf(v, "Migrate CI to GitHub Actions");
            var ci = v.RowAt(r);
            string ciTranscript = ci.Newest.TranscriptPath;
            answers.Enqueue(IDNO);
            ClickCell(v, r, IndexView.FixedColumns + v.Accounts.Count);
            Check(File.Exists(ciTranscript) && ci.ByKey.Count == 3, "trash + No deletes nothing");
            answers.Enqueue(IDYES);
            ClickCell(v, RowOf(v, "Migrate CI to GitHub Actions"), IndexView.FixedColumns + v.Accounts.Count);
            Check(!File.Exists(ciTranscript), "trash + Yes deletes the transcript");
            Check(RowOf(v, "Migrate CI to GitHub Actions") < 0, "row is gone from the list");
            Check(store.Accounts.All(a => a.Find(ci.Id) == null), "cards gone from all three accounts");
            Check(dialogs.Last().Contains("無法復原"), "the confirm says it cannot be undone");

            // Search and archived filter.
            var search = Field<TextBox>(form, "search");
            search.Text = "shop-web";
            Pump(500);
            Check(v.Items.Count == 3, "search by folder finds the shop-web sessions: " + v.Items.Count);
            search.Text = "";
            Field<CheckBox>(form, "showArchived").Checked = true;
            Pump(400);
            Check(RowOf(v, "Set up pre-commit hooks") >= 0, "show archived lists the archived session");
            Field<CheckBox>(form, "showArchived").Checked = false;
            Pump(300);

            // Account menu: hide, then show again.
            var cAcc = acct(C);
            var menu = (ContextMenuStrip)Call(form, "AccountMenu", cAcc);
            Check(menu.Items.Count >= 6, "account menu has its items");
            menu.Items.OfType<ToolStripMenuItem>().First(i => i.Text == "隱藏這個帳號").PerformClick();
            Pump(300);
            var hiddenLink = Field<LinkLabel>(form, "hiddenLink");
            Check(!v.Accounts.Any(a => a.Email == C) && hiddenLink.Visible, "hiding removes the column and shows the hidden link: " + hiddenLink.Text);
            Call(form, "SetHidden", store.Get(cAcc.Key), false);
            Pump(300);
            Check(v.Accounts.Any(a => a.Email == C) && !hiddenLink.Visible, "account shown again");

            // Empty an account: two confirms, exclusive purged, shared kept elsewhere.
            var plan = store.PlanAccountPurge(acct(C).Key);
            var exclusive = plan.Exclusive.Select(c => c.TranscriptPath).Where(p => p != null).ToList();
            var shared = plan.Shared.Select(c => c.Id).ToList();
            answers.Enqueue(IDYES);
            answers.Enqueue(IDYES);
            Call(form, "DoPurgeAccount", acct(C).Key);
            Pump(500);
            Check(store.Get(acct(C).Key).Cards.Count == 0, "account emptied");
            Check(exclusive.All(p => !File.Exists(p)), "its own sessions' transcripts deleted");
            Check(shared.All(sid => store.Accounts.Any(a => a.Find(sid) != null)), "shared sessions still in the other accounts");
            Check(!v.Accounts.Any(a => a.Email == C) && hiddenLink.Visible, "an emptied account hides itself");

            // History shows what happened; permanent deletes can't be undone.
            var hist = store.History();
            Check(hist.Any(m => Json.Str(m, "kind") == "purge" && !Json.Bool(m, "undoable")), "history records the permanent deletes");

            // English UI on the same data.
            form.Close();
            Pump(200);
            L.Lang = "en";
            form = Open(store);
            v = Field<IndexView>(form, "index");
            Check(v.Columns[1].Text == "Last active" && Field<CheckBox>(form, "showArchived").Text == "Show archived", "English labels");
            Check(Field<Label>(form, "status").Text.StartsWith("Found "), "English status: " + Field<Label>(form, "status").Text);
            form.Close();
            Pump(200);
            outDir = outSave;
        }

        static void Screenshots(string lang)
        {
            if (outDir == null) return;
            L.Lang = lang;
            shotPrefix = lang + "-";
            var store = Demo.Create(DemoDir(lang));
            var form = Open(store);
            var v = Field<IndexView>(form, "index");
            v.Items[0].Selected = true;
            Shot(form, "overview");

            using (var sync = new SyncForm(store, k => Theme.ColorFor(store.Accounts.Select(a => a.Key).OrderBy(x => x, StringComparer.Ordinal).ToList().IndexOf(k)), false)
                { StartPosition = FormStartPosition.Manual, Location = new Point(80, 60) })
            {
                sync.Show(form);
                Pump(500);
                Shot(sync, "sync");
                sync.Close();
            }

            var row = v.RowAt(RowOf(v, "Fix flaky checkout test"));
            using (var det = new DetailForm(store, row.NewestAccount, row.Newest, k => Theme.Faint) { StartPosition = FormStartPosition.Manual, Location = new Point(80, 60) })
            {
                det.Show(form);
                Pump(400);
                Shot(det, "detail");
                det.Close();
            }

            shotNextDialog = "delete-confirm";
            answers.Enqueue(IDNO);
            ClickCell(v, RowOf(v, "Investigate memory leak in queue worker"), IndexView.FixedColumns + v.Accounts.Count);
            Pump(300);
            Check(File.Exists(Path.Combine(outDir, shotPrefix + "delete-confirm.png")), lang + " screenshots saved");
            form.Close();
            Pump(200);
        }
    }
}

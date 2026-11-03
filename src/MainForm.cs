using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SessionSync
{
    class MainForm : Form
    {
        const long BusyMs = 3 * 60 * 1000;

        readonly Store store;
        readonly IndexView index = new IndexView();
        readonly Label banner = new Label();
        readonly TextBox search = new TextBox();
        readonly CheckBox showArchived = new CheckBox();
        readonly LinkLabel hiddenLink = new LinkLabel();
        readonly LinkLabel langLink = new LinkLabel();
        readonly LinkLabel orphanLink = new LinkLabel();
        readonly Button restartBtn = Theme.MakeButton(L.T("重開 Claude"));

        // Set when the language changes; Program then opens a fresh window in that language.
        public bool Relaunch;
        HashSet<string> hidden, shown;
        readonly Label status = new Label();
        readonly LinkLabel linkUndo = new LinkLabel();
        readonly LinkLabel linkOverwrite = new LinkLabel();
        readonly LinkLabel linkDetails = new LinkLabel();
        readonly Label hint = new Label();
        readonly Timer debounce = new Timer { Interval = 600 };
        readonly Timer runningTimer = new Timer { Interval = 4000 };
        readonly Timer searchTimer = new Timer { Interval = 150 };
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();

        bool desktopRunning, loaded;
        int deferred;
        DateTime lastRefresh = DateTime.MinValue;
        Action pendingOverwrite;
        string lastBatch;
        List<Item> lastSkipped = new List<Item>();
        List<SessionRow> shownRows = new List<SessionRow>();

        public MainForm(Store store)
        {
            this.store = store;
            Text = L.T("Session 同步器") + (Store.Demo ? " — Demo" : "");
            Theme.StyleForm(this);
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.S(1280), Theme.S(800));
            MinimumSize = new Size(Theme.S(940), Theme.S(480));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            RestoreWindow();
            hidden = PrefSet("hiddenAccounts");
            shown = PrefSet("shownAccounts");

            BuildIndex();
            BuildStatusBar();
            BuildBanner();
            BuildToolbar();
            Theme.Apply(this);

            debounce.Tick += (s, e) =>
            {
                debounce.Stop();
                // A running session re-sorts rows on every write; don't move rows under the pointer.
                if (index.ClientRectangle.Contains(index.PointToClient(Cursor.Position)) && deferred++ < 20) { debounce.Start(); return; }
                deferred = 0;
                RefreshAll();
            };
            runningTimer.Tick += (s, e) => UpdateRunning();
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); Render(); };
            Shown += (s, e) =>
            {
                // The first scan reads every card from disk; keep the window responsive meanwhile.
                // Later scans hit the cache and stay on the UI thread.
                var ui = TaskScheduler.FromCurrentSynchronizationContext();
                Task.Factory.StartNew(() => { try { store.Scan(); } catch { } })
                    .ContinueWith(t => { loaded = true; RefreshAll(); StartWatching(); runningTimer.Start(); index.Focus(); }, ui);
            };
            Activated += (s, e) => { if (loaded && (DateTime.Now - lastRefresh).TotalSeconds > 2) RefreshAll(); };
            FormClosing += (s, e) => SaveWindow();
            // A language change opens a new window in this process; this one must stop
            // refreshing, or its timers fire on disposed controls.
            FormClosed += (s, e) =>
            {
                debounce.Stop();
                runningTimer.Stop();
                searchTimer.Stop();
                foreach (var w in watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
                watchers.Clear();
            };
        }

        // ---------- layout ----------

        void BuildToolbar()
        {
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = Theme.S(52),
                RowCount = 1,
                ColumnCount = 11,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                BackColor = Theme.Panel,
                Padding = new Padding(Theme.S(12), 0, Theme.S(10), 0),
            };
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 11; i++) bar.ColumnStyles.Add(i == 5 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));

            var brand = new Label { Text = L.T("Session 同步器"), Font = Theme.Big, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Theme.S(14), 0) };
            search.Width = Theme.S(260);
            search.Anchor = AnchorStyles.Left;
            search.Margin = new Padding(0, 0, Theme.S(12), 0);
            Native.CueBanner(search, L.T("搜尋標題或資料夾（Ctrl+F）"));
            search.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };

            showArchived.Text = L.T("顯示已封存");
            showArchived.AutoSize = true;
            showArchived.Anchor = AnchorStyles.Left;
            showArchived.Checked = store.Pref("showArchived") == "1";
            showArchived.CheckedChanged += (s, e) => { store.SetPref("showArchived", showArchived.Checked ? "1" : "0"); Render(); };

            hiddenLink.AutoSize = true;
            hiddenLink.Anchor = AnchorStyles.Left;
            hiddenLink.LinkBehavior = LinkBehavior.HoverUnderline;
            hiddenLink.Margin = new Padding(Theme.S(14), 0, 0, 0);
            hiddenLink.Visible = false;
            hiddenLink.LinkClicked += (s, e) => ShowHiddenMenu();

            orphanLink.AutoSize = true;
            orphanLink.Anchor = AnchorStyles.Left;
            orphanLink.LinkBehavior = LinkBehavior.HoverUnderline;
            orphanLink.Margin = new Padding(Theme.S(14), 0, 0, 0);
            orphanLink.Visible = false;
            orphanLink.LinkClicked += (s, e) => OpenOrphans();

            restartBtn.Anchor = AnchorStyles.Right;
            restartBtn.Visible = false;
            restartBtn.Click += (s, e) => RestartClaude();

            langLink.AutoSize = true;
            langLink.Anchor = AnchorStyles.Right;
            langLink.LinkBehavior = LinkBehavior.HoverUnderline;
            langLink.Margin = new Padding(Theme.S(12), 0, 0, 0);
            langLink.Text = L.Names[L.Lang] + " ▾";
            langLink.LinkClicked += (s, e) => ShowLanguageMenu();

            var sync = Theme.MakeButton(L.T("完整同步…"), "primary");
            sync.Anchor = AnchorStyles.Right;
            sync.Click += (s, e) => OpenSync();
            var hist = Theme.MakeButton(L.T("操作紀錄"));
            hist.Anchor = AnchorStyles.Right;
            hist.Click += (s, e) => OpenHistory();
            var refresh = Theme.MakeButton(L.T("重新整理"));
            refresh.Anchor = AnchorStyles.Right;
            refresh.Click += (s, e) => RefreshAll();
            var tip = new ToolTip();
            tip.SetToolTip(refresh, "F5");
            tip.SetToolTip(sync, L.T("讓所有帳號都有同一組 session，先預覽再套用"));

            bar.Controls.Add(brand, 0, 0);
            bar.Controls.Add(search, 1, 0);
            bar.Controls.Add(showArchived, 2, 0);
            bar.Controls.Add(hiddenLink, 3, 0);
            bar.Controls.Add(orphanLink, 4, 0);
            bar.Controls.Add(restartBtn, 6, 0);
            bar.Controls.Add(sync, 7, 0);
            bar.Controls.Add(hist, 8, 0);
            bar.Controls.Add(refresh, 9, 0);
            bar.Controls.Add(langLink, 10, 0);
            bar.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, bar.Height - 1, bar.Width, bar.Height - 1); };
            Controls.Add(bar);
        }

        void BuildBanner()
        {
            banner.Dock = DockStyle.Top;
            banner.Height = Theme.S(30);
            banner.BackColor = Theme.WarnBg;
            banner.ForeColor = Theme.WarnText;
            banner.Padding = new Padding(Theme.S(14), 0, Theme.S(14), 0);
            banner.TextAlign = ContentAlignment.MiddleLeft;
            banner.AutoEllipsis = true;
            banner.Text = L.T("Claude 桌面版正在執行：加進去的 session 要重開 Claude 才會出現在側欄。要移除或覆蓋「目前登入」帳號的卡片，建議先關閉 Claude，不然它可能把卡片寫回去。");
            banner.Visible = false;
            Controls.Add(banner);
        }

        void BuildStatusBar()
        {
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = Theme.S(32),
                RowCount = 1,
                ColumnCount = 6,
                BackColor = Theme.Panel,
                Padding = new Padding(Theme.S(12), 0, Theme.S(12), 0),
            };
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++) bar.ColumnStyles.Add(i == 4 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            status.AutoSize = true;
            status.Anchor = AnchorStyles.Left;
            status.MaximumSize = new Size(Theme.S(760), Theme.S(30));
            status.AutoEllipsis = true;
            status.Text = L.T("讀取中…");
            status.ForeColor = Theme.Muted;
            foreach (var l in new[] { linkUndo, linkOverwrite, linkDetails })
            {
                l.AutoSize = true;
                l.Anchor = AnchorStyles.Left;
                l.Visible = false;
                l.LinkBehavior = LinkBehavior.HoverUnderline;
                l.Margin = new Padding(Theme.S(10), 0, 0, 0);
            }
            linkUndo.Text = L.T("復原");
            linkUndo.LinkClicked += (s, e) => { if (lastBatch != null) UndoBatch(lastBatch); };
            linkOverwrite.LinkClicked += (s, e) => { var a = pendingOverwrite; pendingOverwrite = null; if (a != null) a(); };
            linkDetails.Text = L.T("略過明細");
            linkDetails.LinkClicked += (s, e) => ShowSkipped();
            hint.AutoSize = true;
            hint.Anchor = AnchorStyles.Right;
            hint.ForeColor = Theme.Faint;
            hint.Font = Theme.Small;
            hint.Text = L.T("點圓點＝加入／移除　🗑＝永久刪除　←→＋空白鍵＝鍵盤操作　點帳號名稱＝帳號選單　雙擊＝詳細");
            bar.Controls.Add(status, 0, 0);
            bar.Controls.Add(linkUndo, 1, 0);
            bar.Controls.Add(linkOverwrite, 2, 0);
            bar.Controls.Add(linkDetails, 3, 0);
            bar.Controls.Add(hint, 5, 0);
            bar.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
            Controls.Add(bar);
        }

        void BuildIndex()
        {
            index.Dock = DockStyle.Fill;
            index.ColorOfKey = k => colors.ContainsKey(k) ? colors[k] : Theme.Faint;
            index.CellClicked += (row, a, state) =>
            {
                if (state == CellState.Present) DoRemove(a.Key, new List<string> { row.Id });
                else DoTransfer(row.NewestAccount.Key, a.Key, new List<string> { row.Id }, false, false);
            };
            index.DeleteClicked += row => DoPurge(new List<string> { row.Id });
            index.AccountHeaderClicked += (a, at) => ShowAccountMenu(a, at);
            index.DoubleClick += (s, e) =>
            {
                if (index.IsActionColumnAt(index.PointToClient(Cursor.Position))) return;
                var r = index.SelectedRows();
                if (r.Count == 1) ShowDetail(r[0].NewestAccount, r[0].Newest);
            };
            index.KeyDown += (s, e) =>
            {
                var r = index.SelectedRows();
                if (e.KeyCode == Keys.Enter && r.Count == 1) { ShowDetail(r[0].NewestAccount, r[0].Newest); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete && e.Shift && r.Count > 0) { DoPurge(r.Select(x => x.Id).ToList()); e.Handled = true; }
                else if (e.KeyCode == Keys.A && e.Control) { foreach (ListViewItem it in index.Items) it.Selected = true; e.Handled = true; }
            };
            index.ContextMenuStrip = IndexMenu();
            Controls.Add(index);
        }

        static ContextMenuStrip NewMenu()
        {
            return new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new MenuColors()), ForeColor = Theme.Text, Font = Theme.Ui };
        }

        static Bitmap Swatch(Color c)
        {
            int s = Theme.S(12);
            var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            using (var b = new SolidBrush(c))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(b, 1, 1, s - 3, s - 3);
            }
            return bmp;
        }

        Color ColorOf(Account a) { return colors.ContainsKey(a.Key) ? colors[a.Key] : Theme.Faint; }

        // ---------- which accounts show ----------

        HashSet<string> PrefSet(string key)
        {
            return new HashSet<string>((store.Pref(key) ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }

        // An account shows when it has sessions (or you asked to see it) and you haven't hidden it.
        bool IsVisible(Account a)
        {
            return !hidden.Contains(a.LabelKey) && (a.Cards.Count > 0 || shown.Contains(a.LabelKey));
        }

        List<Account> VisibleAccounts() { return store.Accounts.Where(IsVisible).ToList(); }

        // Real accounts that are out of view; folders the app made but never used stay out of this list.
        List<Account> HiddenAccounts() { return store.Accounts.Where(a => a.InUse && !IsVisible(a)).ToList(); }

        List<Account> UnusedAccounts() { return store.Accounts.Where(a => !a.InUse && !IsVisible(a)).ToList(); }

        void SetHidden(Account a, bool hide)
        {
            if (hide) { hidden.Add(a.LabelKey); shown.Remove(a.LabelKey); }
            else
            {
                hidden.Remove(a.LabelKey);
                if (a.Cards.Count == 0) shown.Add(a.LabelKey);
            }
            store.SetPref("hiddenAccounts", string.Join("|", hidden));
            store.SetPref("shownAccounts", string.Join("|", shown));
            Render();
        }

        void ShowHiddenMenu()
        {
            var m = NewMenu();
            var list = HiddenAccounts();
            foreach (var a in list)
            {
                var acct = a;
                string why = hidden.Contains(a.LabelKey) ? L.F("{0} 個 session", a.Cards.Count) : L.T("沒有 session");
                m.Items.Add(new ToolStripMenuItem(L.F("顯示「{0}」（{1}）", a.Name, why), Swatch(ColorOf(a)), (x, y) => SetHidden(acct, false)));
            }
            if (list.Count > 1)
            {
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem(L.T("全部顯示"), null, (x, y) => { foreach (var a in list) SetHidden(a, false); }));
            }
            var unused = UnusedAccounts();
            if (unused.Count > 0)
            {
                if (list.Count > 0) m.Items.Add(new ToolStripSeparator());
                var more = new ToolStripMenuItem(L.T("沒用過的帳號資料夾"));
                foreach (var a in unused)
                {
                    var acct = a;
                    more.DropDownItems.Add(new ToolStripMenuItem(a.Name + " · " + a.Short, Swatch(ColorOf(a)), (x, y) => SetHidden(acct, false)));
                }
                more.DropDown.Renderer = m.Renderer;
                more.DropDown.ForeColor = Theme.Text;
                m.Items.Add(more);
            }
            m.Closed += (s, e) => BeginInvoke((Action)(() => m.Dispose()));
            m.Show(hiddenLink, new Point(0, hiddenLink.Height));
        }

        ContextMenuStrip IndexMenu()
        {
            var m = NewMenu();
            m.Opening += (s, e) =>
            {
                m.Items.Clear();
                var rows = index.SelectedRows();
                if (rows.Count == 0) { e.Cancel = true; return; }
                string n = rows.Count > 1 ? L.F(" {0} 個", rows.Count) : "";
                if (rows.Count == 1)
                {
                    var r0 = rows[0];
                    m.Items.Add(new ToolStripMenuItem(L.T("詳細資料"), null, (x, y) => ShowDetail(r0.NewestAccount, r0.Newest)) { ShortcutKeyDisplayString = "Enter", Font = Theme.UiBold });
                    m.Items.Add(new ToolStripSeparator());
                }
                var targets = VisibleAccounts();
                m.Items.Add(new ToolStripMenuItem(L.T("加到所有帳號") + n, null, (x, y) => AddEverywhere(rows, targets)));
                var addTo = new ToolStripMenuItem(L.T("加到") + n);
                var removeFrom = new ToolStripMenuItem(L.T("從…移除") + n);
                foreach (var a in VisibleAccounts())
                {
                    var acct = a;
                    int need = rows.Count(r => { Card x; return !r.ByKey.TryGetValue(acct.Key, out x) || Store.IsBehind(x, r.Newest); });
                    int present = rows.Count(r => r.ByKey.ContainsKey(acct.Key));
                    addTo.DropDownItems.Add(new ToolStripMenuItem(a.Name + (rows.Count > 1 ? L.F("（{0} 個）", need) : ""), Swatch(ColorOf(a)),
                        (x, y) => AddEverywhere(rows, new List<Account> { acct })) { Enabled = need > 0 });
                    removeFrom.DropDownItems.Add(new ToolStripMenuItem(a.Name + (rows.Count > 1 ? L.F("（{0} 個）", present) : ""), Swatch(ColorOf(a)),
                        (x, y) => DoRemove(acct.Key, rows.Where(r => r.ByKey.ContainsKey(acct.Key)).Select(r => r.Id).ToList())) { Enabled = present > 0 });
                }
                foreach (var dd in new[] { addTo, removeFrom }) { dd.DropDown.Renderer = m.Renderer; dd.DropDown.ForeColor = Theme.Text; }
                m.Items.Add(addTo);
                m.Items.Add(removeFrom);
                if (rows.Count == 1)
                {
                    var r0 = rows[0];
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add(new ToolStripMenuItem(L.T("顯示對話檔"), null, (x, y) => Reveal(r0.Newest.TranscriptPath)) { Enabled = r0.Newest.TranscriptExists });
                    m.Items.Add(new ToolStripMenuItem(L.T("在檔案總管顯示卡片"), null, (x, y) => Reveal(store.CardPath(r0.NewestAccount, r0.Id))));
                }
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem(L.T("永久刪除對話") + n + "…", null, (x, y) => DoPurge(rows.Select(r => r.Id).ToList()))
                    { ShortcutKeyDisplayString = "Shift+Del", ForeColor = Theme.Danger });
            };
            return m;
        }

        // Menu under an account's column header.
        void ShowAccountMenu(Account a, Point screen)
        {
            var m = AccountMenu(a);
            m.Closed += (s, e) => BeginInvoke((Action)(() => m.Dispose()));
            m.Show(screen);
        }

        ContextMenuStrip AccountMenu(Account a)
        {
            var m = NewMenu();
            m.Items.Add(new ToolStripMenuItem(a.Name, Swatch(ColorOf(a))) { Enabled = false });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(L.T("改名…"), null, (x, y) => Rename(a)));
            m.Items.Add(new ToolStripMenuItem(L.T("隱藏這個帳號"), null, (x, y) => SetHidden(a, true)));
            // Everything listed right now that this account lacks or has an older copy of.
            var rows = shownRows.Where(r => r.Newest.TranscriptExists).ToList();
            int need = rows.Count(r => { Card c; return r.ByKey.TryGetValue(a.Key, out c) ? Store.IsBehind(c, r.Newest) : !a.Tombstones.ContainsKey(r.Id); });
            m.Items.Add(new ToolStripMenuItem(L.F("把列表中的 session 都加進來（{0} 個）", need), null, (x, y) =>
                AddEverywhere(rows.Where(r => r.ByKey.ContainsKey(a.Key) || !a.Tombstones.ContainsKey(r.Id)).ToList(), new List<Account> { a }))
                { Enabled = need > 0 });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(L.T("刪除這個帳號的所有 session…"), null, (x, y) => DoPurgeAccount(a.Key))
                { ForeColor = Theme.Danger, Enabled = a.Cards.Count > 0 });
            return m;
        }

        void ShowLanguageMenu()
        {
            var m = NewMenu();
            foreach (var code in L.Codes)
            {
                var c = code;
                m.Items.Add(new ToolStripMenuItem(L.Names[c], null, (x, y) =>
                {
                    if (c == L.Lang) return;
                    store.SetPref("lang", c);
                    L.Lang = c;
                    Relaunch = true;
                    Close();
                }) { Checked = c == L.Lang });
            }
            m.Closed += (s, e) => BeginInvoke((Action)(() => m.Dispose()));
            m.Show(langLink, new Point(0, langLink.Height));
        }

        // ---------- data ----------

        void RefreshAll()
        {
            if (IsDisposed || Disposing) return;
            try { store.Scan(); }
            catch (Exception e) { ShowStatus(L.F("讀取失敗：{0}", e.Message), Theme.Danger); return; }
            lastRefresh = DateTime.Now;
            UpdateRunning();
            Render();
            if (status.Text == L.T("讀取中…"))
            {
                int cards = store.Accounts.Sum(a => a.Cards.Count);
                int sessions = SessionRow.Build(store.Accounts).Count;
                int unknown = store.Accounts.Count(a => a.Cards.Count > 0 && !a.Known);
                ShowStatus(L.F("找到 {0} 個帳號、{1} 個 session（{2} 張卡片）", store.Accounts.Count(a => a.Cards.Count > 0), sessions, cards)
                    + (unknown > 0 ? L.F("。{0} 個帳號還沒認出：用它登入 Claude 後開一次本程式，或點名稱自己取", unknown) : ""), Theme.Muted);
            }
        }

        void UpdateRunning()
        {
            desktopRunning = Store.DesktopRunning();
            restartBtn.Visible = desktopRunning;
            UpdateBanner();
        }

        void UpdateBanner()
        {
            if (store.ReadOnly)
            {
                banner.Text = L.F("偵測到不認得的 Claude 資料格式（{0}），為了安全目前只能瀏覽、不能修改。Claude 更新後可能會這樣，請等本程式更新。", store.FormatIssues[0]);
                banner.BackColor = Theme.Danger;
                banner.ForeColor = Color.White;
                banner.Visible = true;
            }
            else
            {
                banner.Text = L.T("Claude 桌面版正在執行：加進去的 session 要重開 Claude 才會出現在側欄。要移除或覆蓋「目前登入」帳號的卡片，建議先關閉 Claude，不然它可能把卡片寫回去。");
                banner.BackColor = Theme.WarnBg;
                banner.ForeColor = Theme.WarnText;
                banner.Visible = desktopRunning;
            }
        }

        // Read-only mode: say why instead of doing anything.
        bool Locked()
        {
            if (!store.ReadOnly) return false;
            ShowStatus(L.F("唯讀模式：{0}", store.FormatIssues[0]), Theme.Danger);
            return true;
        }

        void Render()
        {
            var all = store.Accounts.Select(a => a.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            colors.Clear();
            for (int i = 0; i < all.Count; i++) colors[all[i]] = Theme.ColorFor(i);
            string q = search.Text.Trim().ToLowerInvariant();
            var visible = VisibleAccounts();
            var keys = new HashSet<string>(visible.Select(a => a.Key));
            shownRows = SessionRow.Build(store.Accounts).Where(r => r.ByKey.Keys.Any(keys.Contains)
                && (showArchived.Checked || !r.Newest.Archived)
                && (q.Length == 0 || (r.Newest.Title + " " + r.Newest.Cwd).ToLowerInvariant().Contains(q))).ToList();
            index.SetData(visible, shownRows);
            int orphans = store.OrphanIds.Count;
            orphanLink.Visible = orphans > 0;
            orphanLink.Text = L.F("沒在清單裡的對話（{0}）", orphans);
            UpdateBanner();
            int n = HiddenAccounts().Count;
            hiddenLink.Visible = n > 0;
            hiddenLink.Text = L.F("隱藏的帳號（{0}）", n) + " ▾";
        }

        // ---------- operations ----------

        // Adds each session to every listed account that lacks it (or has an older copy), as one undoable batch.
        void AddEverywhere(List<SessionRow> rows, List<Account> targets)
        {
            if (Locked()) return;
            var ops = new List<Item>();
            int left = 0;
            foreach (var r in rows)
                foreach (var a in targets)
                {
                    Card c;
                    bool has = r.ByKey.TryGetValue(a.Key, out c);
                    if (has && !Store.IsBehind(c, r.Newest)) continue;
                    // Bulk adds don't bring back what was deleted there or can't be resumed;
                    // a click on that one cell still does.
                    if (!has && (a.Tombstones.ContainsKey(r.Id) || !r.Newest.TranscriptExists)) { left++; continue; }
                    ops.Add(new Item { Id = r.Id, Title = r.Newest.Title, From = r.NewestAccount.Key, To = a.Key, Type = has ? "update" : "add" });
                }
            string note = left > 0 ? L.F("，{0} 個在該帳號刪除過或對話檔遺失，沒有加入", left) : "";
            if (ops.Count == 0) { ShowStatus(L.T("所選的 session 在這些帳號都已經是最新的") + note, Theme.Muted); return; }
            try
            {
                Report(store.ApplySync(ops, L.F("加入 {0} 張卡片", ops.Count)), "已加入 {0} 個 session", "沒有加入任何 session", null, null);
                status.Text += note;
            }
            catch (Exception e) { ShowStatus(e.Message, Theme.Danger); }
            RefreshAll();
        }

        void DoTransfer(string from, string to, List<string> ids, bool move, bool overwrite)
        {
            if (Locked()) return;
            Account src, dst;
            try { src = store.Get(from); dst = store.Get(to); }
            catch (Exception e) { ShowStatus(e.Message, Theme.Danger); return; }
            UpdateRunning();
            if (desktopRunning && ((move && src.Current) || (overwrite && dst.Current)))
            {
                var which = move && src.Current ? src : dst;
                var ok = MessageBox.Show(this,
                    L.F("「{0}」是目前登入的帳號，而且 Claude 正在執行。\n改動它已經載入的卡片時，Claude 之後可能又把卡片寫回去。\n\n建議先關閉 Claude 再做。仍要繼續嗎？", which.Name),
                    L.T("Claude 正在執行"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (ok != DialogResult.Yes) return;
            }
            try
            {
                var r = store.Transfer(from, to, ids, move, overwrite);
                Action<List<string>> retry = null;
                if (!overwrite) retry = again => DoTransfer(from, to, again, move, true);
                Report(r, move ? "已搬移 {0} 個 session" : "已複製 {0} 個 session", move ? "沒有搬移任何 session" : "沒有複製任何 session", dst, retry);
            }
            catch (Exception e) { ShowStatus(e.Message, Theme.Danger); }
            RefreshAll();
        }

        void DoRemove(string from, List<string> ids)
        {
            if (Locked()) return;
            Account a;
            try { a = store.Get(from); } catch (Exception e) { ShowStatus(e.Message, Theme.Danger); return; }
            var cards = ids.Select(a.Find).Where(c => c != null).ToList();
            if (cards.Count == 0) return;
            UpdateRunning();
            var sb = new StringBuilder();
            sb.AppendLine(L.F("要從「{0}」移除這 {1} 個 session 嗎？", a.Name, cards.Count));
            sb.AppendLine();
            foreach (var c in cards.Take(10)) sb.AppendLine(L.Bullet + c.Title);
            if (cards.Count > 10) sb.AppendLine(L.Bullet + "…");
            sb.AppendLine();
            sb.AppendLine(L.T("只會移除這個帳號側欄裡的卡片。對話檔不會刪，其他帳號不受影響，之後可以在「操作紀錄」復原。"));
            int lonely = cards.Count(c => c.AlsoIn.Count == 0);
            if (lonely > 0)
            {
                sb.AppendLine();
                sb.AppendLine(L.F("注意：其中 {0} 個只存在這個帳號。移除後，任何帳號的側欄都看不到它（對話檔還在，可以復原）。", lonely));
            }
            if (desktopRunning && a.Current)
            {
                sb.AppendLine();
                sb.AppendLine(L.T("Claude 正在執行，而且這是目前登入的帳號。Claude 可能把卡片寫回去，建議先關閉 Claude。"));
            }
            if (MessageBox.Show(this, sb.ToString(), L.T("移除 session"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { Report(store.Remove(from, ids), "已移除 {0} 個 session", "沒有移除任何 session", null, null); }
            catch (Exception e) { ShowStatus(e.Message, Theme.Danger); }
            RefreshAll();
        }

        // A session counts as busy when its transcript was written in the last few minutes
        // (any Claude: desktop, CLI, IDE), or the desktop app says it was active.
        bool Busy(long lastActivityAt, long transcriptWriteMs)
        {
            long now = Fs.NowMs();
            return now - transcriptWriteMs < BusyMs || (desktopRunning && now - lastActivityAt < BusyMs);
        }

        static long WriteMs(string path)
        {
            try { return path == null || !File.Exists(path) ? 0 : (long)(File.GetLastWriteTimeUtc(path) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
            catch { return 0; }
        }

        bool RefuseBusy(IEnumerable<string> titles)
        {
            var t = titles.FirstOrDefault();
            if (t == null) return false;
            MessageBox.Show(this, L.F("「{0}」最近 3 分鐘內還有活動，可能正在執行。\n\n請先在 Claude 停止它（或關閉 Claude），再刪除。", t),
                L.T("先停止這個 session"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        // Deletes the conversation files for good, after one clear confirmation.
        void DoPurge(List<string> ids)
        {
            if (Locked()) return;
            List<PurgeTarget> plan;
            try { plan = store.PlanPurge(ids); }
            catch (Exception e) { ShowStatus(e.Message, Theme.Danger); return; }
            if (plan.Count == 0) return;
            UpdateRunning();
            if (RefuseBusy(plan.Where(t => Busy(t.LastActivityAt, t.TranscriptWriteMs)).Select(t => t.Title))) return;
            var sb = new StringBuilder();
            sb.AppendLine(L.F("永久刪除這 {0} 個 session 的對話？", plan.Count));
            sb.AppendLine();
            foreach (var t in plan.Take(10))
                sb.AppendLine(L.Bullet + t.Title + L.F("　（{0}，在 {1} 個帳號）", Theme.Bytes(t.Bytes), t.Cards.Select(c => c.Key.Key).Distinct().Count()));
            if (plan.Count > 10) sb.AppendLine(L.Bullet + "…");
            var also = plan.SelectMany(t => t.AlsoDeleted).Distinct().ToList();
            if (also.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(L.F("另外 {0} 個共用同一個對話檔的 session 也會一起刪除：", also.Count));
                foreach (var t in also.Take(5)) sb.AppendLine(L.Bullet + t);
            }
            sb.AppendLine();
            sb.AppendLine(L.T("會刪除："));
            sb.AppendLine(L.T("・所有帳號側欄裡的這些 session"));
            sb.AppendLine(L.T("・對話檔（.jsonl）和它的附屬資料夾"));
            sb.AppendLine(L.T("・這些 session 的檔案編輯備份（file-history）"));
            sb.AppendLine();
            sb.AppendLine(L.T("刪除後無法復原，也不會進資源回收筒。專案資料夾裡的檔案不受影響。"));
            if (desktopRunning) sb.AppendLine("\n" + L.T("Claude 正在執行：側欄要重開 Claude 才會更新。"));
            if (MessageBox.Show(this, sb.ToString(), L.T("永久刪除對話"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                var r = store.Purge(ids);
                Report(r, "已永久刪除 {0} 個 session 的對話", "沒有刪除任何 session", null, null);
                if (r.Done.Count > 0) status.Text += L.F("，釋放 {0}", Theme.Bytes(r.Bytes));
                linkUndo.Visible = false;
            }
            catch (Exception e) { ShowStatus(L.F("刪除時出錯：{0}", e.Message), Theme.Danger); }
            RefreshAll();
        }

        // Empties one account: sessions only it has are deleted for good,
        // sessions other accounts also have are only taken off this account.
        void DoPurgeAccount(string key)
        {
            if (Locked()) return;
            AccountPurgePlan p;
            try { p = store.PlanAccountPurge(key); }
            catch (Exception e) { ShowStatus(e.Message, Theme.Danger); return; }
            var a = p.Account;
            if (p.Exclusive.Count + p.Shared.Count == 0) return;
            UpdateRunning();
            if (RefuseBusy(p.Exclusive.Concat(p.Shared).Where(c => Busy(c.LastActivityAt, WriteMs(c.TranscriptPath))).Select(c => c.Title))) return;

            var sb = new StringBuilder();
            sb.AppendLine(L.F("刪除「{0}」的所有 session？", a.Name));
            sb.AppendLine();
            if (p.Exclusive.Count > 0)
            {
                sb.AppendLine(L.F("只在這個帳號的 {0} 個：永久刪除，連同對話檔，無法復原", p.Exclusive.Count));
                foreach (var c in p.Exclusive.Take(8)) sb.AppendLine("　" + L.Bullet + c.Title);
                if (p.Exclusive.Count > 8) sb.AppendLine("　" + L.Bullet + "…");
                sb.AppendLine();
            }
            if (p.Shared.Count > 0)
            {
                sb.AppendLine(L.F("其他帳號也有的 {0} 個：只從這個帳號移除，其他帳號照常可用（可以復原）", p.Shared.Count));
                sb.AppendLine();
            }
            sb.AppendLine(L.T("帳號本身、排程任務和專案資料夾都不會動。"));
            if (desktopRunning && a.Current) sb.AppendLine("\n" + L.T("Claude 正在執行，而且這是目前登入的帳號，建議先關閉 Claude。"));
            if (MessageBox.Show(this, sb.ToString(), L.T("清空帳號"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (p.Exclusive.Count > 0 && MessageBox.Show(this, L.F("再確認一次：永久刪除 {0} 個對話，刪了就找不回來。", p.Exclusive.Count),
                L.T("清空帳號"), MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                var r = store.PurgeAccount(key);
                int purged = r.Done.Count(d => d.Type == "purged"), removed = r.Done.Count(d => d.Type == "removed");
                ShowStatus(L.F("已清空「{0}」：永久刪除 {1} 個、移除 {2} 個，釋放 {3}", a.Name, purged, removed, Theme.Bytes(r.Bytes)), r.Skipped.Count > 0 ? Theme.WarnText : Theme.Ok);
                if (r.Skipped.Count > 0)
                {
                    lastSkipped = r.Skipped;
                    status.Text += L.F("，略過 {0} 個（{1}）", r.Skipped.Count, r.Skipped[0].Reason + (r.Skipped.Count > 1 ? "…" : ""));
                    linkDetails.Visible = true;
                }
            }
            catch (Exception e) { ShowStatus(L.F("刪除時出錯：{0}", e.Message), Theme.Danger); }
            RefreshAll();
        }

        // The status-bar link toggles: after an undo it offers redo, after a redo it offers undo again.
        void UndoBatch(string id)
        {
            if (Locked()) return;
            try
            {
                var m = Undo(this, store, id);
                if (m != null)
                {
                    string reverted = Json.Str(m, "reverts"), redoId = Json.Str(m, "redoId");
                    if (reverted != null)
                    {
                        ShowStatus(L.F("已重做：{0}", Json.Str(m, "revertsLabel")), Theme.Ok);
                        lastBatch = reverted;
                        linkUndo.Text = L.T("復原");
                        linkUndo.Visible = true;
                    }
                    else
                    {
                        ShowStatus(L.F("已復原：{0}", Json.Str(m, "label")), Theme.Ok);
                        lastBatch = redoId;
                        linkUndo.Text = L.T("重做");
                        linkUndo.Visible = redoId != null;
                    }
                }
            }
            catch (Exception e) { ShowStatus(L.F("無法復原：{0}", e.Message), Theme.Danger); }
            RefreshAll();
        }

        // Undo, asking first when files changed after the operation. Null when the user declines.
        public static Dictionary<string, object> Undo(IWin32Window owner, Store store, string id)
        {
            try { return store.Undo(id); }
            catch (ChangedSinceException e)
            {
                var ok = MessageBox.Show(owner, L.F("有 {0} 個檔案在這次操作之後又被改過（例如你在 Claude 裡繼續了那個 session）。\n\n仍要復原嗎？那些之後的變更會被蓋掉。", e.Paths.Count),
                    L.T("復原"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                return ok == DialogResult.Yes ? store.Undo(id, true) : null;
            }
        }

        // doneFormat / noneFormat are table keys, e.g. "已複製 {0} 個 session".
        void Report(OpResult r, string doneFormat, string noneFormat, Account dst, Action<List<string>> retry)
        {
            string msg = r.Done.Count > 0
                ? L.F(doneFormat, r.Done.Count) + (dst != null ? L.F("到「{0}」", dst.Name) : "")
                : L.T(noneFormat);
            if (r.Skipped.Count > 0) msg += L.F("，略過 {0} 個（{1}）", r.Skipped.Count, r.Skipped[0].Reason + (r.Skipped.Count > 1 ? "…" : ""));
            if (r.Done.Count > 0 && dst != null && dst.Current && desktopRunning) msg += L.T("。重開 Claude 後才會出現在側欄");
            lastBatch = r.BatchId;
            lastSkipped = r.Skipped;
            var over = r.Skipped.Where(s => s.CanOverwrite).Select(s => s.Id).ToList();
            pendingOverwrite = retry != null && over.Count > 0 ? (Action)(() => retry(over)) : null;
            ShowStatus(msg, r.Done.Count > 0 ? Theme.Ok : Theme.WarnText);
            linkUndo.Text = L.T("復原");
            linkUndo.Visible = r.BatchId != null;
            linkOverwrite.Text = L.F("仍要覆蓋 {0} 個", over.Count);
            linkOverwrite.Visible = pendingOverwrite != null;
            linkDetails.Visible = r.Skipped.Count > 0;
        }

        void ShowStatus(string text, Color color)
        {
            status.Text = text;
            status.ForeColor = color;
            linkUndo.Visible = linkOverwrite.Visible = linkDetails.Visible = false;
        }

        void ShowSkipped()
        {
            var sb = new StringBuilder();
            foreach (var s in lastSkipped.Take(40)) sb.AppendLine(L.Bullet + s.Title + L.T("：") + s.Reason);
            if (lastSkipped.Count > 40) sb.AppendLine(L.F("・…還有 {0} 個", lastSkipped.Count - 40));
            MessageBox.Show(this, sb.ToString(), L.T("略過的 session"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void Rename(Account a)
        {
            if (a == null) return;
            string v = Prompt.Ask(this, L.T("帳號名稱"),
                L.F("給這個帳號取個好認的名字（只存在這台電腦）。\n{0}\n留空就用預設名稱。", a.Email ?? a.AccountUuid), a.Label ?? "");
            if (v == null) return;
            try { store.SetLabel(a.Key, v); } catch (Exception e) { ShowStatus(e.Message, Theme.Danger); }
            RefreshAll();
        }

        void Reveal(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { ShowStatus(L.T("找不到檔案"), Theme.Danger); return; }
            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        // ---------- dialogs ----------

        void ShowDetail(Account a, Card card)
        {
            UpdateRunning();
            using (var f = new DetailForm(store, a, card, k => colors.ContainsKey(k) ? colors[k] : Theme.Faint))
            {
                f.ShowDialog(this);
                if (f.CopyTargets.Count > 0)
                {
                    var ops = f.CopyTargets.Select(to => new Item { Id = card.Id, Title = card.Title, From = a.Key, To = to, Type = "add" }).ToList();
                    try { Report(store.ApplySync(ops, L.F("加入 {0} 張卡片", ops.Count)), "已複製 {0} 個 session", "沒有複製任何 session", null, null); }
                    catch (Exception e) { ShowStatus(e.Message, Theme.Danger); }
                    RefreshAll();
                }
                else if (f.RemoveRequested)
                {
                    DoRemove(a.Key, new List<string> { card.Id });
                }
                else if (f.PurgeRequested)
                {
                    DoPurge(new List<string> { card.Id });
                }
            }
        }

        void OpenOrphans()
        {
            using (var f = new OrphanForm(store, VisibleAccounts(), k => colors.ContainsKey(k) ? colors[k] : Theme.Faint))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Chosen.Count == 0) { RefreshAll(); return; }
                if (f.Action == "delete") DoPurgeOrphans(f.Chosen);
                else if (f.Action == "recover") DoRecover(f.Chosen, f.Target);
            }
        }

        void DoPurgeOrphans(List<Orphan> chosen)
        {
            if (Locked()) return;
            var sb = new StringBuilder();
            sb.AppendLine(L.F("永久刪除這 {0} 個沒在清單裡的對話？", chosen.Count));
            sb.AppendLine();
            foreach (var o in chosen.Take(10)) sb.AppendLine(L.Bullet + (o.Title ?? L.T("(沒有標題)")) + "　(" + Theme.Bytes(o.Size) + ")");
            if (chosen.Count > 10) sb.AppendLine(L.Bullet + "…");
            sb.AppendLine();
            sb.AppendLine(L.T("會刪掉對話檔、附屬資料夾和 file-history 備份。如果它們是命令列或 VS Code 還要用的對話，那邊也會跟著消失。"));
            sb.AppendLine(L.T("刪除後無法復原，也不會進資源回收筒。專案資料夾裡的檔案不受影響。"));
            if (MessageBox.Show(this, sb.ToString(), L.T("永久刪除對話"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                var r = store.PurgeOrphans(chosen.Select(o => o.Id).ToList());
                Report(r, "已永久刪除 {0} 個對話", "沒有刪除任何 session", null, null);
                if (r.Done.Count > 0) status.Text += L.F("，釋放 {0}", Theme.Bytes(r.Bytes));
                linkUndo.Visible = false;
            }
            catch (Exception e) { ShowStatus(L.F("刪除時出錯：{0}", e.Message), Theme.Danger); }
            RefreshAll();
        }

        void DoRecover(List<Orphan> chosen, string toKey)
        {
            if (Locked()) return;
            Account dst;
            try { dst = store.Get(toKey); } catch (Exception e) { ShowStatus(e.Message, Theme.Danger); return; }
            var ok = MessageBox.Show(this, L.F("把 {0} 個對話加進「{1}」的清單？\n\n這是實驗功能：會用這個帳號最新的一張卡片當範本產生新卡片。重開 Claude 後確認能不能接著聊；不行的話，可以在操作紀錄復原。", chosen.Count, dst.Name),
                L.T("加進帳號"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (ok != DialogResult.Yes) return;
            try { Report(store.Recover(chosen.Select(o => o.Id).ToList(), toKey), "已加入 {0} 個 session", "沒有加入任何 session", dst, null); }
            catch (Exception e) { ShowStatus(e.Message, Theme.Danger); }
            RefreshAll();
        }

        void RestartClaude()
        {
            var processes = ClaudeApp.DesktopProcesses();
            if (processes.Count == 0) { UpdateRunning(); return; }
            string exe = ClaudeApp.ExePath(processes);
            if (MessageBox.Show(this, L.T("重開 Claude 會中斷正在進行的 session（包括正在回答到一半的）。要重開嗎？"), L.T("重開 Claude"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            UseWaitCursor = true;
            Application.DoEvents();
            try
            {
                if (!ClaudeApp.CloseAll(processes, 10000))
                {
                    UseWaitCursor = false;
                    if (MessageBox.Show(this, L.T("Claude 沒有在 10 秒內關閉（可能縮到系統匣了）。要強制結束嗎？"), L.T("重開 Claude"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    UseWaitCursor = true;
                    ClaudeApp.KillAll(processes);
                }
                if (exe != null) ClaudeApp.Launch(exe);
                ShowStatus(exe != null ? L.T("已重開 Claude") : L.T("已關閉 Claude，請自己再打開它"), Theme.Ok);
            }
            catch (Exception e) { ShowStatus(e.Message, Theme.Danger); }
            finally { UseWaitCursor = false; }
            UpdateRunning();
        }

        void OpenSync()
        {
            if (Locked()) return;
            RefreshAll();
            using (var f = new SyncForm(store, k => colors.ContainsKey(k) ? colors[k] : Theme.Faint, desktopRunning, IsVisible))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Selected.Count == 0) { RefreshAll(); return; }
                try { Report(store.ApplySync(f.Selected), "已同步 {0} 個 session", "沒有同步任何 session", null, null); }
                catch (Exception e) { ShowStatus(e.Message, Theme.Danger); }
            }
            RefreshAll();
        }

        void OpenHistory()
        {
            using (var f = new HistoryForm(store))
            {
                f.ShowDialog(this);
                if (f.Changed) { ShowStatus(L.T("已從操作紀錄復原"), Theme.Ok); RefreshAll(); }
            }
        }

        // ---------- watching ----------

        void StartWatching()
        {
            foreach (var root in store.Roots.Where(Directory.Exists))
            {
                try
                {
                    var w = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                        SynchronizingObject = this,
                    };
                    FileSystemEventHandler kick = (s, e) => { debounce.Stop(); debounce.Start(); };
                    w.Changed += kick;
                    w.Created += kick;
                    w.Deleted += kick;
                    w.Renamed += (s, e) => { debounce.Stop(); debounce.Start(); };
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch { /* manual refresh still works */ }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5) { RefreshAll(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
            if (keyData == Keys.Escape && search.Focused && search.Text.Length > 0) { search.Clear(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void RestoreWindow()
        {
            var b = store.Pref("window");
            if (string.IsNullOrEmpty(b)) return;
            var p = b.Split(',').Select(x => { int v; return int.TryParse(x, out v) ? v : int.MinValue; }).ToArray();
            if (p.Length != 5 || p.Contains(int.MinValue)) return;
            var r = new Rectangle(p[0], p[1], p[2], p[3]);
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r))) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
            if (p[4] == 1) WindowState = FormWindowState.Maximized;
        }

        void SaveWindow()
        {
            var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            store.SetPref("window", r.X + "," + r.Y + "," + r.Width + "," + r.Height + "," + (WindowState == FormWindowState.Maximized ? 1 : 0));
        }
    }

    class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
        public override Color MenuBorder { get { return Theme.LineStrong; } }
        public override Color MenuItemBorder { get { return Theme.Sel; } }
        public override Color MenuItemSelected { get { return Theme.Sel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Sel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Sel; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.Line; } }
    }
}

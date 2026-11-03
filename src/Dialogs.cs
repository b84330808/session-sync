using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SessionSync
{
    static class Ui
    {
        public static Form Dialog(string title, int w, int h)
        {
            var f = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(Theme.S(w), Theme.S(h)),
                MinimumSize = new Size(Theme.S(Math.Min(w, 420)), Theme.S(Math.Min(h, 300))),
                MinimizeBox = false,
                ShowIcon = false,
                ShowInTaskbar = false,
            };
            Theme.StyleForm(f);
            return f;
        }

        public static Label Text(string text, Font font = null, Color? color = null)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = font ?? Theme.Ui,
                ForeColor = color ?? Theme.Text,
                Margin = new Padding(0, 0, 0, Theme.S(6)),
            };
        }

        public static FlowLayoutPanel Buttons(params Control[] buttons)
        {
            var p = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(10), Theme.S(10)),
                BackColor = Theme.Bg,
            };
            p.Controls.AddRange(buttons);
            return p;
        }

        public static ListView List(params object[] columns)
        {
            var lv = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Dock = DockStyle.Fill,
            };
            for (int i = 0; i < columns.Length; i += 2) lv.Columns.Add((string)columns[i], Theme.S((int)columns[i + 1]));
            return lv;
        }
    }

    // ---------- one session ----------

    class DetailForm : Form
    {
        public readonly List<string> CopyTargets = new List<string>();
        public bool RemoveRequested, PurgeRequested;

        // Pads to a column in a monospaced font, counting wide (CJK) characters as two.
        static string PadWide(string s, int width)
        {
            int w = s.Sum(ch => ch >= 0x1100 && (ch <= 0x115f || (ch >= 0x2e80 && ch <= 0xa4cf) || (ch >= 0xac00 && ch <= 0xd7a3) || (ch >= 0xf900 && ch <= 0xfaff) || (ch >= 0xfe30 && ch <= 0xfe4f) || (ch >= 0xff00 && ch <= 0xff60) || (ch >= 0xffe0 && ch <= 0xffe6)) ? 2 : 1);
            return s + new string(' ', Math.Max(1, width - w));
        }

        public DetailForm(Store store, Account acct, Card card, Func<string, Color> colorOf)
        {
            var f = this;
            Text = L.T("Session 詳細資料");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Theme.S(620), Theme.S(640));
            MinimumSize = new Size(Theme.S(460), Theme.S(420));
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            Theme.StyleForm(this);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), 0) };
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 58));

            var title = Ui.Text(card.Title, Theme.Big);
            title.MaximumSize = new Size(Theme.S(560), 0);
            body.Controls.Add(title);
            body.Controls.Add(Ui.Text(L.F("在「{0}」", acct.Name) + (card.Archived ? L.T(" · 已封存") : ""), Theme.Small, Theme.Muted));
            body.Controls.Add(Ui.Text(L.T("各帳號的狀態（勾選要複製過去的帳號）"), Theme.UiBold, Theme.Muted));

            var presence = Ui.List(L.T("帳號"), 240, L.T("狀態"), 300);
            presence.CheckBoxes = true;
            foreach (var a in store.Accounts.Where(x => x.InUse))
            {
                var other = a.Find(card.Id);
                string state;
                if (a.Key == acct.Key) state = L.T("正在看的這份");
                else if (other != null)
                {
                    state = L.F("有 · {0}", Theme.When(other.LastActivityAt))
                        + L.T(Store.IsBehind(other, card) ? "（比正在看的這份舊）" : Store.IsBehind(card, other) ? "（比正在看的這份新）" : "（相同）");
                }
                else if (a.Tombstones.ContainsKey(card.Id)) state = L.F("刪除過 · {0}", Theme.When(a.Tombstones[card.Id]));
                else state = L.T("沒有");
                var it = new ListViewItem(new[] { "● " + a.Name, state }) { Tag = a.Key, UseItemStyleForSubItems = false };
                it.SubItems[0].ForeColor = colorOf(a.Key);
                it.SubItems[1].ForeColor = a.Key == acct.Key ? Theme.Faint : Theme.Text;
                presence.Items.Add(it);
            }
            presence.ItemCheck += (s, e) => { if ((string)presence.Items[e.Index].Tag == acct.Key) e.NewValue = CheckState.Unchecked; };
            body.Controls.Add(presence);

            var copyBtn = Theme.MakeButton(L.T("複製到勾選的帳號"), "primary");
            copyBtn.Enabled = false;
            presence.ItemChecked += (s, e) => copyBtn.Enabled = presence.CheckedItems.Count > 0;
            copyBtn.Click += (s, e) =>
            {
                CopyTargets.AddRange(presence.CheckedItems.Cast<ListViewItem>().Select(i => (string)i.Tag));
                Close();
            };
            var copyRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(6), 0, Theme.S(8)) };
            copyRow.Controls.Add(copyBtn);
            body.Controls.Add(copyRow);
            body.Controls.Add(Ui.Text(L.T("資料"), Theme.UiBold, Theme.Muted));

            Dictionary<string, object> raw = null;
            try { raw = store.ReadRaw(acct, card.Id); } catch { }
            var connectors = new List<string>();
            if (raw != null && raw.ContainsKey("remoteMcpServersConfig") && raw["remoteMcpServersConfig"] is System.Collections.IEnumerable)
                foreach (var x in (System.Collections.IEnumerable)raw["remoteMcpServersConfig"])
                {
                    var name = Json.Str(x as Dictionary<string, object>, "name");
                    if (name != null) connectors.Add(name);
                }
            var sb = new StringBuilder();
            Action<string, string> row = (k, v) => sb.AppendLine(PadWide(k, 14) + (v ?? "—"));
            row(L.T("資料夾"), card.Cwd);
            row(L.T("模型"), card.Model + (string.IsNullOrEmpty(card.Effort) ? "" : " · " + card.Effort));
            row(L.T("輪數"), card.CompletedTurns.ToString());
            row(L.T("建立"), Theme.Full(card.CreatedAt));
            row(L.T("最後活動"), Theme.Full(card.LastActivityAt));
            row(L.T("權限模式"), raw == null ? null : Json.Str(raw, "permissionMode"));
            row(L.T("對話檔"), card.TranscriptExists ? Theme.Bytes(card.TranscriptSize) + "  " + card.TranscriptPath : L.T("找不到，接不回"));
            row("Connector", connectors.Count > 0 ? string.Join(L.T("、"), connectors) : null);
            row("sessionId", card.SessionId);
            row("cliSessionId", card.CliSessionId);
            row(L.T("卡片檔"), store.CardPath(acct, card.Id));
            var info = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Dock = DockStyle.Fill,
                Font = Theme.Mono,
                Text = sb.ToString(),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Dark ? Theme.Card : Color.White,
                ForeColor = Theme.Text,
            };
            body.Controls.Add(info);

            var close = Theme.MakeButton(L.T("關閉"));
            close.Click += (s, e) => Close();
            var remove = Theme.MakeButton(L.T("從這個帳號移除"), "danger");
            remove.Click += (s, e) => { RemoveRequested = true; Close(); };
            var purge = Theme.MakeButton(L.T("永久刪除對話…"), "danger");
            purge.Click += (s, e) => { PurgeRequested = true; Close(); };
            var showTranscript = Theme.MakeButton(L.T("顯示對話檔"));
            showTranscript.Enabled = card.TranscriptExists;
            showTranscript.Click += (s, e) => Process.Start("explorer.exe", "/select,\"" + card.TranscriptPath + "\"");
            var showCard = Theme.MakeButton(L.T("在檔案總管顯示卡片"));
            showCard.Click += (s, e) => Process.Start("explorer.exe", "/select,\"" + store.CardPath(acct, card.Id) + "\"");
            Controls.Add(body);
            Controls.Add(Ui.Buttons(close, purge, remove, showTranscript, showCard));
            CancelButton = close;
            Theme.Apply(this);
            f.Shown += (s, e) => presence.Focus();
        }
    }

    // ---------- full sync preview ----------

    class SyncForm : Form
    {
        public List<Item> Selected = new List<Item>();
        readonly Store store;
        readonly Func<string, Color> colorOf;
        readonly bool running;
        readonly FlowLayoutPanel accounts = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        readonly CheckBox optTomb = new CheckBox { Text = L.T("不要加回在該帳號刪過的 session"), Checked = true, AutoSize = true };
        readonly CheckBox optMissing = new CheckBox { Text = L.T("略過對話檔已遺失的 session"), Checked = true, AutoSize = true };
        readonly CheckBox optArchived = new CheckBox { Text = L.T("包含已封存的 session"), AutoSize = true };
        readonly ListView ops = Ui.List(L.T("動作"), 70, "Session", 330, L.T("加到"), 180, L.T("來源"), 180);
        readonly ListView skipped = Ui.List("Session", 300, L.T("帳號"), 170, L.T("原因"), 230);
        readonly Label warn = new Label();
        readonly Label skippedLabel = Ui.Text("");
        readonly Label foot = new Label();
        readonly Button apply = Theme.MakeButton(L.T("套用"), "primary");
        bool filling;

        // isVisible: accounts hidden in the main window start unchecked.
        public SyncForm(Store store, Func<string, Color> colorOf, bool running, Func<Account, bool> isVisible = null)
        {
            this.store = store;
            this.colorOf = colorOf;
            this.running = running;
            Text = L.T("完整同步");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Theme.S(820), Theme.S(700));
            MinimumSize = new Size(Theme.S(560), Theme.S(480));
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            Theme.StyleForm(this);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), 0) };
            for (int i = 0; i < 6; i++) body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

            var intro = Ui.Text(L.T("讓勾選的帳號都有同一組 session。同一個 session 在好幾個帳號時，以「最後活動」較新的那份為準。不會刪除任何卡片，套用後可以在「操作紀錄」整批復原。"), Theme.Ui, Theme.Muted);
            intro.MaximumSize = new Size(Theme.S(760), 0);
            body.Controls.Add(intro, 0, 0);
            body.Controls.Add(Ui.Text(L.T("參與的帳號"), Theme.UiBold, Theme.Muted), 0, 1);
            foreach (var a in store.Accounts.Where(x => x.InUse))
            {
                bool on = a.Cards.Count > 0 && (isVisible == null || isVisible(a));
                var cb = new CheckBox { Text = L.F("{0}（{1}）", a.Name, a.Cards.Count), Tag = a.Key, Checked = on, AutoSize = true, ForeColor = colorOf(a.Key), Margin = new Padding(0, 0, Theme.S(14), Theme.S(4)) };
                cb.CheckedChanged += (s, e) => Recompute();
                accounts.Controls.Add(cb);
            }
            body.Controls.Add(accounts, 0, 2);
            var optRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)) };
            foreach (var cb in new[] { optTomb, optMissing, optArchived })
            {
                cb.Margin = new Padding(0, 0, Theme.S(16), 0);
                cb.CheckedChanged += (s, e) => Recompute();
                optRow.Controls.Add(cb);
            }
            body.Controls.Add(optRow, 0, 3);
            warn.AutoSize = true;
            warn.MaximumSize = new Size(Theme.S(760), 0);
            warn.BackColor = Theme.WarnBg;
            warn.ForeColor = Theme.WarnText;
            warn.Padding = new Padding(Theme.S(8), Theme.S(5), Theme.S(8), Theme.S(5));
            warn.Margin = new Padding(0, 0, 0, Theme.S(6));
            warn.Visible = false;
            warn.Text = L.T("Claude 正在執行。加進目前登入帳號的 session 要重開 Claude 才看得到；更新目前帳號裡已載入的卡片，可能會被 Claude 蓋回去，建議先關閉 Claude。");
            // Rows are fixed: a hidden control would otherwise let the next ones move up a row.
            body.Controls.Add(warn, 0, 4);
            body.Controls.Add(Ui.Text(L.T("會做的變更（取消勾選就不做）"), Theme.UiBold, Theme.Muted), 0, 5);
            ops.CheckBoxes = true;
            ops.ItemChecked += (s, e) => { if (!filling) UpdateFoot(); };
            body.Controls.Add(ops, 0, 6);
            skippedLabel.Font = Theme.UiBold;
            skippedLabel.ForeColor = Theme.Muted;
            skippedLabel.Margin = new Padding(0, Theme.S(10), 0, Theme.S(6));
            body.Controls.Add(skippedLabel, 0, 7);
            body.Controls.Add(skipped, 0, 8);

            var cancel = Theme.MakeButton(L.T("取消"));
            cancel.DialogResult = DialogResult.Cancel;
            apply.Click += (s, e) =>
            {
                Selected = ops.CheckedItems.Cast<ListViewItem>().Select(i => (Item)i.Tag).ToList();
                DialogResult = DialogResult.OK;
            };
            foot.AutoSize = true;
            foot.ForeColor = Theme.Muted;
            foot.Margin = new Padding(0, Theme.S(7), Theme.S(12), 0);
            var buttons = Ui.Buttons(apply, cancel, foot);
            Controls.Add(body);
            Controls.Add(buttons);
            AcceptButton = apply;
            CancelButton = cancel;
            Theme.Apply(this);
            Load += (s, e) => Recompute();
        }

        void Recompute()
        {
            var targets = accounts.Controls.OfType<CheckBox>().Where(c => c.Checked).Select(c => (string)c.Tag).ToList();
            filling = true;
            ops.BeginUpdate();
            skipped.BeginUpdate();
            ops.Items.Clear();
            skipped.Items.Clear();
            SyncPlan plan = null;
            string error = null;
            if (targets.Count < 2) error = L.T("至少勾選兩個帳號。");
            else
            {
                try
                {
                    plan = store.PlanSync(new SyncOptions
                    {
                        Targets = targets,
                        RespectTombstones = optTomb.Checked,
                        SkipMissingTranscript = optMissing.Checked,
                        IncludeArchived = optArchived.Checked,
                    });
                }
                catch (Exception e) { error = e.Message; }
            }
            Func<string, string> nameOf = k => { var a = store.Accounts.FirstOrDefault(x => x.Key == k); return a == null ? k : a.Name; };
            if (plan != null)
            {
                foreach (var o in plan.Ops)
                {
                    var it = new ListViewItem(new[] { L.T(o.Type == "add" ? "＋ 新增" : "↻ 更新"), o.Title, nameOf(o.To), nameOf(o.From) })
                    {
                        Tag = o,
                        Checked = true,
                        UseItemStyleForSubItems = false,
                    };
                    it.SubItems[0].ForeColor = o.Type == "add" ? Theme.Ok : Theme.Accent;
                    it.SubItems[2].ForeColor = colorOf(o.To);
                    it.SubItems[3].ForeColor = Theme.Muted;
                    ops.Items.Add(it);
                }
                foreach (var s in plan.Skipped)
                    skipped.Items.Add(new ListViewItem(new[] { s.Title, nameOf(s.To), s.Reason }) { ForeColor = Theme.Muted });
            }
            ops.EndUpdate();
            skipped.EndUpdate();
            filling = false;
            skippedLabel.Text = plan == null ? "" : L.F("略過 {0} 項", plan.Skipped.Count);
            warn.Visible = running && plan != null && plan.Ops.Any(o => { var a = store.Accounts.FirstOrDefault(x => x.Key == o.To); return a != null && a.Current; });
            if (error != null) foot.Text = error;
            else if (plan.Ops.Count == 0) foot.Text = L.T("這些帳號已經同步好了。");
            UpdateFoot(error != null || plan.Ops.Count == 0);
        }

        void UpdateFoot(bool keepText = false)
        {
            int n = ops.CheckedItems.Count;
            apply.Enabled = n > 0;
            apply.Text = n > 0 ? L.F("套用 {0} 項", n) : L.T("套用");
            if (!keepText) foot.Text = n > 0 ? L.F("會改動 {0} 張卡片，全部可以復原", n) : L.T("沒有勾選任何變更");
        }
    }

    // ---------- history ----------

    class HistoryForm : Form
    {
        public bool Changed;
        readonly Store store;
        readonly ListView list = Ui.List(L.T("時間"), 150, L.T("操作"), 420, L.T("檔案"), 60, L.T("狀態"), 80);

        public HistoryForm(Store store)
        {
            this.store = store;
            Text = L.T("操作紀錄");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Theme.S(780), Theme.S(520));
            MinimumSize = new Size(Theme.S(480), Theme.S(320));
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            Theme.StyleForm(this);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), 0) };
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var intro = Ui.Text(L.T("每次複製、搬移、移除、同步之前，都會先備份它要改的檔案。復原會把那些檔案放回操作前的狀態。"), Theme.Ui, Theme.Muted);
            intro.MaximumSize = new Size(Theme.S(720), 0);
            body.Controls.Add(intro);
            list.MultiSelect = false;
            body.Controls.Add(list);

            var close = Theme.MakeButton(L.T("關閉"));
            close.DialogResult = DialogResult.Cancel;
            var undo = Theme.MakeButton(L.T("復原所選"), "primary");
            undo.Enabled = false;
            list.SelectedIndexChanged += (s, e) =>
            {
                var m = list.SelectedItems.Count == 1 ? (Dictionary<string, object>)list.SelectedItems[0].Tag : null;
                undo.Enabled = m != null && !Json.Bool(m, "undone") && Undoable(m);
            };
            list.DoubleClick += (s, e) => { if (undo.Enabled) undo.PerformClick(); };
            undo.Click += (s, e) => UndoSelected();
            var open = Theme.MakeButton(L.T("開啟備份資料夾"));
            open.Click += (s, e) => { Directory.CreateDirectory(store.HistoryDir); Process.Start("explorer.exe", "\"" + store.HistoryDir + "\""); };
            Controls.Add(body);
            Controls.Add(Ui.Buttons(close, undo, open));
            CancelButton = close;
            Theme.Apply(this);
            Load += (s, e) => Fill();
        }

        static bool Undoable(Dictionary<string, object> m) { return !m.ContainsKey("undoable") || Json.Bool(m, "undoable"); }

        void Fill()
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var m in store.History())
            {
                bool undone = Json.Bool(m, "undone");
                var it = new ListViewItem(new[]
                {
                    Theme.Full(Json.Long(m, "createdAt")),
                    Json.Str(m, "label"),
                    Journal.Entries(m).Count.ToString(),
                    undone ? L.T("已復原") : !Undoable(m) ? L.T("無法復原") : Json.Bool(m, "incomplete") ? L.T("未完成") : "",
                }) { Tag = m, ForeColor = undone ? Theme.Faint : !Undoable(m) ? Theme.Danger : Theme.Text };
                list.Items.Add(it);
            }
            if (list.Items.Count == 0) list.Items.Add(new ListViewItem(new[] { "", L.T("還沒有任何操作") }) { ForeColor = Theme.Faint });
            list.EndUpdate();
        }

        void UndoSelected()
        {
            if (list.SelectedItems.Count != 1) return;
            var m = list.SelectedItems[0].Tag as Dictionary<string, object>;
            if (m == null) return;
            var ok = MessageBox.Show(this, L.F("復原「{0}」？\n\n會把這次改到的 {1} 個檔案放回操作前的狀態。", Json.Str(m, "label"), Journal.Entries(m).Count),
                L.T("復原"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok != DialogResult.Yes) return;
            try { if (MainForm.Undo(this, store, Json.Str(m, "id")) != null) Changed = true; }
            catch (Exception e) { MessageBox.Show(this, e.Message, L.T("無法復原"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            Fill();
        }
    }

    // ---------- small text prompt ----------

    static class Prompt
    {
        public static string Ask(IWin32Window owner, string title, string message, string value)
        {
            using (var f = Ui.Dialog(title, 440, 230))
            {
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), 0) };
                body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var msg = Ui.Text(message, Theme.Ui, Theme.Muted);
                msg.MaximumSize = new Size(Theme.S(400), 0);
                var box = new TextBox { Text = value, Dock = DockStyle.Fill, MaxLength = 40, Margin = new Padding(0, Theme.S(6), 0, 0) };
                body.Controls.Add(msg);
                body.Controls.Add(box);
                var ok = Theme.MakeButton(L.T("儲存"), "primary");
                ok.DialogResult = DialogResult.OK;
                var cancel = Theme.MakeButton(L.T("取消"));
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(body);
                f.Controls.Add(Ui.Buttons(ok, cancel));
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                Theme.Apply(f);
                f.Shown += (s, e) => { box.Focus(); box.SelectAll(); };
                return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}

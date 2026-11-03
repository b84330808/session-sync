using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SessionSync
{
    // One session, wherever it lives.
    class SessionRow
    {
        public string Id;
        public Card Newest;
        public Account NewestAccount;
        public readonly Dictionary<string, Card> ByKey = new Dictionary<string, Card>();

        public static List<SessionRow> Build(IEnumerable<Account> accounts)
        {
            var rows = new Dictionary<string, SessionRow>();
            foreach (var a in accounts)
                foreach (var c in a.Cards.Where(c => c.Broken == null))
                {
                    SessionRow r;
                    if (!rows.TryGetValue(c.Id, out r)) rows[c.Id] = r = new SessionRow { Id = c.Id };
                    r.ByKey[a.Key] = c;
                    if (r.Newest == null || Store.Compare(c, r.Newest) > 0) { r.Newest = c; r.NewestAccount = a; }
                }
            return rows.Values.OrderByDescending(r => r.Newest.LastActivityAt).ToList();
        }
    }

    enum CellState { Absent, Deleted, Present, Older }

    // The index: every session once, one column per account, a delete button last.
    // Click a cell to add the session to that account or remove it from there.
    class IndexView : ListView
    {
        public const int FixedColumns = 3;
        public List<Account> Accounts = new List<Account>();
        public Func<string, Color> ColorOfKey = k => Theme.Faint;
        public event Action<SessionRow, Account, CellState> CellClicked;
        public event Action<SessionRow> DeleteClicked;
        public event Action<Account, Point> AccountHeaderClicked;
        readonly ToolTip tip = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };
        string tipKey, signature;
        Point hoverCell = new Point(-1, -1);
        // Column the keyboard acts on in the focused row (Left/Right to move, Space to click).
        public int FocusColumn = -1;
        static readonly Font IconFont = PickIconFont();

        public IndexView()
        {
            View = View.Details;
            OwnerDraw = true;
            FullRowSelect = true;
            MultiSelect = true;
            HideSelection = false;
            HeaderStyle = ColumnHeaderStyle.Clickable;
            BorderStyle = BorderStyle.None;
            DoubleBuffered = true;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            // Row height comes from the small image list.
            SmallImageList = new ImageList { ImageSize = new Size(1, Theme.S(44)) };
        }

        static Font PickIconFont()
        {
            foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                using (var f = new Font(name, 11f))
                    if (f.Name == name) return new Font(name, 11f);
            return null;
        }

        public static string ShortCwd(string cwd)
        {
            if (string.IsNullOrEmpty(cwd)) return "";
            var m = Regex.Match(cwd, @"scratch-workspaces[\\/].*[\\/](scratch-[^\\/]+)$", RegexOptions.IgnoreCase);
            return m.Success ? L.F("暫存 · {0}", m.Groups[1].Value) : cwd;
        }

        int TrashColumn { get { return FixedColumns + Accounts.Count; } }

        // Dots and the trash act on one click; only the other columns open details on double-click.
        public bool IsActionColumnAt(Point p) { return CellAt(p).X >= FixedColumns; }

        Account AccountAt(int column)
        {
            int i = column - FixedColumns;
            return i >= 0 && i < Accounts.Count ? Accounts[i] : null;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Theme.Dark) Native.DarkList(this);
            FitColumns();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitColumns();
        }

        // The session column takes whatever the fixed and account columns leave.
        void FitColumns()
        {
            if (Columns.Count == 0 || !IsHandleCreated) return;
            int others = 0;
            for (int i = 1; i < Columns.Count; i++) others += Columns[i].Width;
            int w = ClientSize.Width - others - 1;
            Columns[0].Width = Math.Max(Theme.S(280), w);
        }

        public SessionRow RowAt(int index) { return (SessionRow)Items[index].Tag; }

        public List<SessionRow> SelectedRows() { return SelectedItems.Cast<ListViewItem>().Select(i => (SessionRow)i.Tag).ToList(); }

        public void SetData(List<Account> accounts, List<SessionRow> rows)
        {
            string colSig = string.Join("|", accounts.Select(a => a.Key + "~" + a.Name));
            string sig = colSig + "\n" + string.Join("\n", rows.Select(r => r.Id + "|" + r.Newest.Title + "|" + r.Newest.LastActivityAt + "|"
                + string.Join(",", r.ByKey.Select(kv => kv.Key + ":" + kv.Value.LastActivityAt + ":" + kv.Value.CompletedTurns)) + "|"
                + string.Join(",", accounts.Where(a => a.Tombstones.ContainsKey(r.Id)).Select(a => a.Key))));
            Accounts = accounts;
            if (sig == signature && Items.Count == rows.Count)
            {
                for (int i = 0; i < rows.Count; i++) Items[i].Tag = rows[i];
                Invalidate();
                return;
            }
            signature = sig;
            var selected = new HashSet<string>(SelectedRows().Select(r => r.Id));
            string topId = TopItem != null ? ((SessionRow)TopItem.Tag).Id : null;
            BeginUpdate();
            bool sameColumns = Columns.Count == FixedColumns + accounts.Count + 1
                && Columns.Cast<ColumnHeader>().Skip(FixedColumns).Take(accounts.Count).Select(c => c.Tag as string).SequenceEqual(accounts.Select(a => a.Key));
            if (!sameColumns)
            {
                Columns.Clear();
                Columns.Add("Session", Theme.S(420));
                Columns.Add(L.T("最後活動"), Theme.S(110));
                Columns.Add(L.T("輪數"), Theme.S(64), HorizontalAlignment.Right);
                foreach (var a in accounts) Columns.Add(new ColumnHeader { Text = a.Name, Width = Theme.S(accounts.Count > 4 ? 140 : accounts.Count > 2 ? 205 : 240), TextAlign = HorizontalAlignment.Center, Tag = a.Key });
                Columns.Add(new ColumnHeader { Text = "", Width = Theme.S(48) });
            }
            else
            {
                for (int i = 0; i < accounts.Count; i++) Columns[FixedColumns + i].Text = accounts[i].Name;
            }
            Items.Clear();
            var items = rows.Select(r =>
            {
                var it = new ListViewItem(r.Newest.Title) { Tag = r };
                for (int i = 1; i <= FixedColumns + accounts.Count; i++) it.SubItems.Add("");
                return it;
            }).ToArray();
            Items.AddRange(items);
            foreach (ListViewItem it in Items) if (selected.Contains(((SessionRow)it.Tag).Id)) it.Selected = true;
            EndUpdate();
            FitColumns();
            int top = topId == null ? -1 : rows.FindIndex(x => x.Id == topId);
            if (top > 0) { try { TopItem = Items[top]; } catch { } }
        }

        public CellState StateOf(SessionRow r, Account a)
        {
            Card c;
            if (r.ByKey.TryGetValue(a.Key, out c)) return Store.IsBehind(c, r.Newest) ? CellState.Older : CellState.Present;
            return a.Tombstones.ContainsKey(r.Id) ? CellState.Deleted : CellState.Absent;
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Dark ? Theme.Card : Theme.Bg)) g.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Line))
            {
                g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                if (e.ColumnIndex != TrashColumn) g.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
            }
            var r = e.Bounds;
            r.Inflate(-Theme.S(8), 0);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var a = AccountAt(e.ColumnIndex);
            if (a != null)
            {
                using (var dot = new SolidBrush(ColorOfKey(a.Key)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillEllipse(dot, r.X, r.Y + (r.Height - Theme.S(8)) / 2, Theme.S(8), Theme.S(8));
                }
                r.X += Theme.S(12);
                r.Width -= Theme.S(24);
                TextRenderer.DrawText(g, (a.Current ? "✓ " : "") + a.Name, Theme.UiBold, r, Theme.Text, flags);
                // A small chevron says the header opens a menu.
                TextRenderer.DrawText(g, "▾", Theme.Small, new Rectangle(r.Right, r.Y, Theme.S(12), r.Height), Theme.Muted, flags);
            }
            else if (e.ColumnIndex < FixedColumns)
            {
                if (e.ColumnIndex == 2) flags |= TextFormatFlags.Right;
                TextRenderer.DrawText(g, e.Header.Text, Theme.UiBold, r, Theme.Muted, flags);
            }
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e) { }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            var g = e.Graphics;
            var row = e.Item.Tag as SessionRow;
            if (row == null) return;
            bool sel = e.Item.Selected;
            Color back = sel ? Theme.Sel : (e.ItemIndex % 2 == 1 ? (Theme.Dark ? Color.FromArgb(0x20, 0x23, 0x29) : Color.FromArgb(0xfa, 0xfb, 0xfc)) : Theme.Panel);
            using (var b = new SolidBrush(back)) g.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var c = row.Newest;
            var r = e.Bounds;
            const TextFormatFlags one = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            Color text = c.Archived ? Theme.Muted : Theme.Text;
            bool hover = hoverCell.X == e.ColumnIndex && hoverCell.Y == e.ItemIndex;

            if (e.ColumnIndex == 0)
            {
                int x = r.X + Theme.S(12), w = r.Width - Theme.S(16);
                int top = r.Y + (r.Height - Theme.S(36)) / 2;
                TextRenderer.DrawText(g, c.Title, Theme.UiBold, new Rectangle(x, top, w, Theme.S(19)), text, one);
                string sub = ShortCwd(c.Cwd);
                var tags = new List<string>();
                if (c.Archived) tags.Add(L.T("已封存"));
                if (!c.TranscriptExists) tags.Add(L.T("對話檔遺失"));
                if (tags.Count > 0) sub = "[" + string.Join("・", tags) + "]  " + sub;
                TextRenderer.DrawText(g, sub, Theme.Mono, new Rectangle(x, top + Theme.S(20), w, Theme.S(16)),
                    !c.TranscriptExists ? Theme.Danger : Theme.Muted, one | TextFormatFlags.PathEllipsis);
                return;
            }
            if (e.ColumnIndex == 1)
            {
                TextRenderer.DrawText(g, Theme.When(c.LastActivityAt), Theme.Ui, Rectangle.Inflate(r, -Theme.S(8), 0), Theme.Muted, one | TextFormatFlags.VerticalCenter);
                return;
            }
            if (e.ColumnIndex == 2)
            {
                TextRenderer.DrawText(g, c.CompletedTurns.ToString(), Theme.Ui, Rectangle.Inflate(r, -Theme.S(8), 0), Theme.Muted, one | TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                return;
            }
            if (e.ColumnIndex == TrashColumn) DrawTrash(g, r, hover);
            else
            {
                var a = AccountAt(e.ColumnIndex);
                if (a != null) DrawCell(g, r, StateOf(row, a), ColorOfKey(a.Key), hover);
            }
            if (e.Item.Focused && e.ColumnIndex == FocusColumn && Focused)
                using (var pen = new Pen(Theme.Accent, 1.5f) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, Rectangle.Inflate(r, -Theme.S(4), -Theme.S(4)));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) && Items.Count > 0)
            {
                int step = e.KeyCode == Keys.Right ? 1 : -1;
                FocusColumn = FocusColumn < FixedColumns ? FixedColumns : Math.Max(FixedColumns, Math.Min(TrashColumn, FocusColumn + step));
                if (FocusedItem == null) { Items[0].Focused = true; Items[0].Selected = true; }
                Invalidate(FocusedItem.Bounds);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Space && FocusedItem != null && FocusColumn >= FixedColumns)
            {
                var row = RowAt(FocusedItem.Index);
                var a = AccountAt(FocusColumn);
                if (a != null) { if (CellClicked != null) CellClicked(row, a, StateOf(row, a)); }
                else if (FocusColumn == TrashColumn && DeleteClicked != null) DeleteClicked(row);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        static void DrawTrash(Graphics g, Rectangle r, bool hover)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Theme.S(26);
            var box = new Rectangle(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
            if (hover)
                using (var p = Theme.Round(box, Theme.S(6)))
                using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 70 : 40, Theme.Danger))) g.FillPath(b, p);
            Color fg = hover ? Theme.Danger : Theme.Faint;
            if (IconFont != null)
            {
                TextRenderer.DrawText(g, "", IconFont, box, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }
            // Fallback bin drawn by hand.
            using (var pen = new Pen(fg, 1.5f))
            {
                int s = Theme.S(12), x = box.X + (d - s) / 2, y = box.Y + (d - s) / 2;
                g.DrawLine(pen, x - 1, y + 2, x + s + 1, y + 2);
                g.DrawRectangle(pen, x + 1, y + 3, s - 2, s - 3);
                g.DrawLine(pen, x + s / 2 - 2, y, x + s / 2 + 2, y);
            }
        }

        static void DrawCell(Graphics g, Rectangle r, CellState state, Color color, bool hover)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Theme.S(16);
            var circle = new Rectangle(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
            if (hover)
            {
                var h = Rectangle.Inflate(circle, Theme.S(6), Theme.S(6));
                using (var p = Theme.Round(h, Theme.S(6)))
                using (var b = new SolidBrush(Theme.Dark ? Color.FromArgb(0x30, 0x35, 0x3d) : Color.FromArgb(0xec, 0xee, 0xf1))) g.FillPath(b, p);
            }
            switch (state)
            {
                case CellState.Present:
                    using (var b = new SolidBrush(color)) g.FillEllipse(b, circle);
                    using (var pen = new Pen(Color.White, Theme.S(2) * 0.9f))
                    {
                        g.DrawLines(pen, new[] {
                            new Point(circle.X + d * 4 / 16, circle.Y + d * 8 / 16),
                            new Point(circle.X + d * 7 / 16, circle.Y + d * 11 / 16),
                            new Point(circle.X + d * 12 / 16, circle.Y + d * 5 / 16) });
                    }
                    break;
                case CellState.Older:
                    using (var pen = new Pen(color, Theme.S(2))) g.DrawEllipse(pen, circle);
                    using (var b = new SolidBrush(color)) g.FillPie(b, circle, 90, 180);
                    break;
                case CellState.Deleted:
                    using (var pen = new Pen(Theme.Faint, Theme.S(2) * 0.8f))
                    {
                        int m = d / 4;
                        g.DrawLine(pen, circle.X + m, circle.Y + m, circle.Right - m, circle.Bottom - m);
                        g.DrawLine(pen, circle.Right - m, circle.Y + m, circle.X + m, circle.Bottom - m);
                    }
                    break;
                default:
                    using (var pen = new Pen(Theme.LineStrong, 1.4f) { DashStyle = hover ? DashStyle.Solid : DashStyle.Dot }) g.DrawEllipse(pen, circle);
                    if (hover)
                        using (var pen = new Pen(color, Theme.S(2)))
                        {
                            g.DrawLine(pen, circle.X + d / 2, circle.Y + d / 4, circle.X + d / 2, circle.Bottom - d / 4);
                            g.DrawLine(pen, circle.X + d / 4, circle.Y + d / 2, circle.Right - d / 4, circle.Y + d / 2);
                        }
                    break;
            }
        }

        Point CellAt(Point p)
        {
            var hit = HitTest(p);
            if (hit.Item == null || hit.SubItem == null) return new Point(-1, -1);
            return new Point(hit.Item.SubItems.IndexOf(hit.SubItem), hit.Item.Index);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var cell = CellAt(e.Location);
            if (cell.X < FixedColumns) cell = new Point(-1, -1);
            if (cell != hoverCell)
            {
                var old = hoverCell;
                hoverCell = cell;
                if (old.Y >= 0 && old.Y < Items.Count) Invalidate(Items[old.Y].Bounds);
                if (cell.Y >= 0) Invalidate(Items[cell.Y].Bounds);
                Cursor = cell.X >= 0 ? Cursors.Hand : Cursors.Default;
            }
            string key = cell.X + ":" + cell.Y;
            if (key == tipKey) return;
            tipKey = key;
            if (cell.X < 0) { tip.Hide(this); return; }
            var row = RowAt(cell.Y);
            var a = AccountAt(cell.X);
            string text = a != null ? TipText(row, a) : L.T("永久刪除這個 session 的對話\n（所有帳號，連同對話檔，無法復原）");
            tip.Show(text, this, e.X + Theme.S(14), e.Y + Theme.S(18), 6000);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            tip.Hide(this);
            tipKey = null;
            if (hoverCell.Y >= 0 && hoverCell.Y < Items.Count) Invalidate(Items[hoverCell.Y].Bounds);
            hoverCell = new Point(-1, -1);
        }

        string TipText(SessionRow row, Account a)
        {
            switch (StateOf(row, a))
            {
                case CellState.Present: return L.F("「{0}」有這個 session（最新）\n點一下＝從這個帳號移除", a.Name);
                case CellState.Older: return L.F("「{0}」有，但比「{1}」的舊\n點一下＝更新成最新版本", a.Name, row.NewestAccount.Name);
                case CellState.Deleted: return L.F("這個 session 在「{0}」刪除過\n點一下＝加回去", a.Name);
                default: return L.F("「{0}」沒有這個 session\n點一下＝加進去", a.Name);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || ModifierKeys != Keys.None) return;
            var cell = CellAt(e.Location);
            if (cell.X < FixedColumns || cell.Y < 0) return;
            FocusColumn = cell.X;
            tip.Hide(this);
            var row = RowAt(cell.Y);
            var a = AccountAt(cell.X);
            if (a != null) { if (CellClicked != null) CellClicked(row, a, StateOf(row, a)); }
            else if (cell.X == TrashColumn && DeleteClicked != null) DeleteClicked(row);
        }

        protected override void OnColumnClick(ColumnClickEventArgs e)
        {
            base.OnColumnClick(e);
            var a = AccountAt(e.Column);
            if (a != null && AccountHeaderClicked != null) AccountHeaderClicked(a, Cursor.Position);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && Items.Count == 0)
            {
                using (var g = CreateGraphics())
                    TextRenderer.DrawText(g, L.T("沒有符合的 session"), Theme.Ui, new Rectangle(0, Theme.S(60), ClientSize.Width, Theme.S(40)), Theme.Faint,
                        TextFormatFlags.HorizontalCenter);
            }
        }
    }
}

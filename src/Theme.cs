using System;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SessionSync
{
    static class Theme
    {
        public static bool Dark;
        public static float Scale = 1f;
        public static Color Bg, Panel, Card, Text, Muted, Faint, Line, LineStrong, Accent, AccentText, Sel, Danger, Ok, WarnBg, WarnText, WarnLine, TagBg;
        public static readonly Color[] Palette = {
            Color.FromArgb(59, 108, 246), Color.FromArgb(224, 130, 61), Color.FromArgb(46, 158, 106), Color.FromArgb(178, 71, 158),
            Color.FromArgb(201, 162, 39), Color.FromArgb(42, 163, 184), Color.FromArgb(212, 82, 74), Color.FromArgb(122, 91, 214) };
        public static Font Ui, UiBold, Title, Small, Mono, Big;

        public static int S(int px) { return (int)Math.Round(px * Scale); }

        public static void Init()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    Dark = v is int && (int)v == 0;
                }
            }
            catch { Dark = false; }
            if (Environment.GetEnvironmentVariable("SESSIONSYNC_THEME") == "light") Dark = false;
            if (Environment.GetEnvironmentVariable("SESSIONSYNC_THEME") == "dark") Dark = true;

            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;

            if (Dark)
            {
                Bg = Rgb(0x15, 0x17, 0x1b); Panel = Rgb(0x1c, 0x1f, 0x24); Card = Rgb(0x24, 0x28, 0x2f);
                Text = Rgb(0xe5, 0xe8, 0xec); Muted = Rgb(0x9b, 0xa4, 0xae); Faint = Rgb(0x6f, 0x78, 0x83);
                Line = Rgb(0x30, 0x35, 0x3d); LineStrong = Rgb(0x45, 0x4c, 0x56);
                Accent = Rgb(0x5b, 0x86, 0xff); AccentText = Color.White; Sel = Rgb(0x26, 0x33, 0x55);
                Danger = Rgb(0xef, 0x6b, 0x60); Ok = Rgb(0x47, 0xc0, 0x7a);
                WarnBg = Rgb(0x3a, 0x2f, 0x12); WarnText = Rgb(0xf3, 0xd6, 0x8d); WarnLine = Rgb(0x5e, 0x4a, 0x17);
                TagBg = Rgb(0x1c, 0x1f, 0x24);
            }
            else
            {
                Bg = Rgb(0xf3, 0xf4, 0xf6); Panel = Color.White; Card = Color.White;
                Text = Rgb(0x1c, 0x21, 0x27); Muted = Rgb(0x66, 0x6f, 0x7a); Faint = Rgb(0x9a, 0xa2, 0xab);
                Line = Rgb(0xe1, 0xe4, 0xe8); LineStrong = Rgb(0xc9, 0xce, 0xd4);
                Accent = Rgb(0x3b, 0x6c, 0xf6); AccentText = Color.White; Sel = Rgb(0xe6, 0xed, 0xfe);
                Danger = Rgb(0xc9, 0x3c, 0x32); Ok = Rgb(0x1f, 0x8a, 0x4c);
                WarnBg = Rgb(0xff, 0xf4, 0xd6); WarnText = Rgb(0x6b, 0x4a, 0x00); WarnLine = Rgb(0xf0, 0xd4, 0x8a);
                TagBg = Rgb(0xf3, 0xf4, 0xf6);
            }

            // A CJK face of the UI language, so kanji take that language's shapes.
            var faces = L.Lang == "ja" ? new[] { "Yu Gothic UI", "Meiryo UI" }
                : L.Lang == "zh" ? new[] { "Microsoft JhengHei UI" }
                : new[] { "Segoe UI" };
            string face = faces.FirstOrDefault(Installed) ?? "Segoe UI";
            Ui = new Font(face, 9.5f);
            UiBold = new Font(face, 9.5f, FontStyle.Bold);
            Title = new Font(face, 10f, FontStyle.Bold);
            Big = new Font(face, 11.5f, FontStyle.Bold);
            Small = new Font(face, 8.5f);
            Mono = new Font(Installed("Cascadia Mono") ? "Cascadia Mono" : "Consolas", 8.5f);
        }

        static Color Rgb(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        static bool Installed(string name)
        {
            using (var f = new Font(name, 9f)) return string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);
        }

        public static Color ColorFor(int index) { return Palette[((index % Palette.Length) + Palette.Length) % Palette.Length]; }

        // ---------- styling ----------

        public static void StyleForm(Form f)
        {
            f.BackColor = Bg;
            f.ForeColor = Text;
            f.Font = Ui;
            f.AutoScaleMode = AutoScaleMode.None;
            f.HandleCreated += (s, e) => DarkTitleBar(f.Handle);
        }

        // Recolors standard controls created in code. Custom-drawn ones paint themselves.
        public static void Apply(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Button)
                {
                    var b = (Button)c;
                    b.FlatStyle = FlatStyle.Flat;
                    b.UseVisualStyleBackColor = false;
                    bool primary = "primary".Equals(b.Tag);
                    bool danger = "danger".Equals(b.Tag);
                    b.BackColor = primary ? Accent : Panel;
                    b.ForeColor = primary ? AccentText : danger ? Danger : Text;
                    b.FlatAppearance.BorderColor = primary ? Accent : LineStrong;
                    b.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(Accent, 0.15f) : (Dark ? Card : Bg);
                    b.Cursor = Cursors.Hand;
                    b.AutoSize = true;
                    b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    b.Padding = new Padding(S(8), S(2), S(8), S(2));
                }
                else if (c is TextBox)
                {
                    c.BackColor = Dark ? Card : Color.White;
                    c.ForeColor = Text;
                    ((TextBox)c).BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is CheckBox || c is RadioButton || c is Label || c is LinkLabel)
                {
                    if (c.ForeColor == SystemColors.ControlText) c.ForeColor = Text;
                    if (c is LinkLabel)
                    {
                        var l = (LinkLabel)c;
                        l.LinkColor = Accent; l.ActiveLinkColor = Accent; l.VisitedLinkColor = Accent;
                    }
                }
                else if (c is ListView && !(c is IndexView))
                {
                    var lv = (ListView)c;
                    lv.BackColor = Panel;
                    lv.ForeColor = Text;
                    lv.BorderStyle = BorderStyle.FixedSingle;
                    if (Dark) lv.HandleCreated += (s, e) => Native.DarkList((ListView)s);
                    // The system header draws black text on the dark theme; paint it ourselves.
                    lv.OwnerDraw = true;
                    lv.DrawColumnHeader += (s, e) => DrawHeader(e);
                    lv.DrawItem += (s, e) => e.DrawDefault = true;
                    lv.DrawSubItem += (s, e) => e.DrawDefault = true;
                }
                if (c.HasChildren) Apply(c);
            }
        }

        public static void DrawHeader(DrawListViewColumnHeaderEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Dark ? Card : Bg)) g.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Line))
            {
                g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                g.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
            }
            var r = Rectangle.Inflate(e.Bounds, -S(6), 0);
            TextRenderer.DrawText(g, e.Header.Text, UiBold, r, Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        public static Button MakeButton(string text, string kind = null)
        {
            return new Button { Text = text, Tag = kind, Margin = new Padding(S(4), S(2), S(4), S(2)) };
        }

        public static void DarkTitleBar(IntPtr hwnd)
        {
            if (!Dark) return;
            int on = 1;
            try
            {
                if (Native.DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) Native.DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch { }
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static string When(long ms)
        {
            if (ms <= 0) return "—";
            var d = Local(ms);
            var now = DateTime.Now;
            double secs = (now - d).TotalSeconds;
            if (secs < 60) return L.T("剛剛");
            if (secs < 3600) return L.F("{0} 分鐘前", (int)(secs / 60));
            string hm = d.ToString("HH:mm");
            if (d.Date == now.Date) return L.F("今天 {0}", hm);
            if (d.Date == now.Date.AddDays(-1)) return L.F("昨天 {0}", hm);
            return d.Year == now.Year ? d.Month + "/" + d.Day + " " + hm : d.ToString("yyyy/M/d");
        }

        public static string Full(long ms) { return ms <= 0 ? "—" : Local(ms).ToString("yyyy/MM/dd HH:mm:ss"); }

        static DateTime Local(long ms) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime(); }

        public static string Bytes(long n)
        {
            if (n < 1024) return n + " B";
            if (n < 1048576) return (n / 1024.0).ToString("0.0") + " KB";
            return (n / 1048576.0).ToString("0.0") + " MB";
        }
    }

    static class Native
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, string lp);
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")]
        public static extern int GetScrollPos(IntPtr hwnd, int bar);
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hwnd, int cmd);

        // Windows 11 dark list + header. Harmless where the theme names are unknown.
        public static void DarkList(ListView lv)
        {
            SetWindowTheme(lv.Handle, "DarkMode_Explorer", null);
            IntPtr header = SendMessage(lv.Handle, 0x101F /* LVM_GETHEADER */, IntPtr.Zero, IntPtr.Zero);
            if (header != IntPtr.Zero) SetWindowTheme(header, "DarkMode_ItemsView", null);
        }

        public static void CueBanner(TextBox t, string text)
        {
            t.HandleCreated += (s, e) => SendMessage(t.Handle, 0x1501, (IntPtr)1, text);
        }
    }
}

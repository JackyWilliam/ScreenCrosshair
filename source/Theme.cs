using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenCrosshair
{
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(13, 18, 25), Card = Color.FromArgb(22, 30, 40),
            Field = Color.FromArgb(29, 40, 53), Border = Color.FromArgb(44, 59, 75), Text = Color.FromArgb(235, 241, 248),
            Muted = Color.FromArgb(151, 166, 185), Accent = Color.FromArgb(95, 225, 192), Error = Color.FromArgb(245, 132, 145);

        public static Label Label(string text, Rectangle bounds, bool muted = false)
        { return new Label { Text = text, Bounds = bounds, ForeColor = muted ? Muted : Text, TextAlign = ContentAlignment.MiddleLeft }; }
        public static Button Button(string text, Rectangle bounds, bool primary = false)
        {
            var button = new ThemeButton { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? Accent : Field, ForeColor = primary ? Background : Text, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(129, 239, 212) : Color.FromArgb(42, 57, 73);
            return button;
        }
        public static void Combo(ComboBox combo, Rectangle bounds, Control parent)
        {
            combo.Bounds = bounds; combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat; combo.BackColor = Field; combo.ForeColor = Text;
            combo.DrawMode = DrawMode.OwnerDrawFixed; combo.ItemHeight = 25; combo.IntegralHeight = false;
            combo.DropDownHeight = 240;
            combo.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? Border : Field)) e.Graphics.FillRectangle(brush, e.Bounds);
                string text = e.Index >= 0 ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
                TextRenderer.DrawText(e.Graphics, text, combo.Font, Rectangle.Inflate(e.Bounds, -8, 0), combo.Enabled ? Text : Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            parent.Controls.Add(combo);
        }
        public static TextBox Input(Rectangle bounds)
        { return new TextBox { Bounds = bounds, BorderStyle = BorderStyle.FixedSingle, BackColor = Field, ForeColor = Text }; }
    }

    public sealed class ThemeButton : Button
    {
        bool hover;
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Enabled && hover ? FlatAppearance.MouseOverBackColor : BackColor);
            if (FlatAppearance.BorderSize > 0)
                using (var pen = new Pen(Focused ? Theme.Accent : FlatAppearance.BorderColor)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    public sealed class DarkComboBox : ComboBox
    {
        void PaintFace(Graphics g)
        {
            using (var brush = new SolidBrush(Theme.Field)) g.FillRectangle(brush, ClientRectangle);
            using (var pen = new Pen(Focused ? Theme.Accent : Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            int arrowWidth = Math.Max(22, Height);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(10, 0, Math.Max(1, Width - arrowWidth - 10), Height), Enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            int x = Width - arrowWidth / 2, y = Height / 2;
            using (var pen = new Pen(Theme.Muted, 1.5F)) g.DrawLines(pen, new[] { new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2) });
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // Keep native keyboard, popup and accessibility behavior while replacing the themed white frame.
            if (m.Msg == 0xF) using (Graphics g = Graphics.FromHwnd(Handle)) PaintFace(g);
            else if ((m.Msg == 0x317 || m.Msg == 0x318) && m.WParam != IntPtr.Zero) using (Graphics g = Graphics.FromHdc(m.WParam)) PaintFace(g);
        }
    }

    public sealed class DarkCheckBox : CheckBox
    {
        public DarkCheckBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            var box = new Rectangle(1, Height / 2 - 8, 16, 16);
            using (var fill = new SolidBrush(Checked ? (Enabled ? Theme.Accent : Theme.Muted) : Theme.Field)) e.Graphics.FillRectangle(fill, box);
            using (var pen = new Pen(Focused ? Theme.Accent : Theme.Border)) e.Graphics.DrawRectangle(pen, box);
            if (Checked) using (var pen = new Pen(Theme.Background, 2)) e.Graphics.DrawLines(pen, new[] { new Point(4, box.Y + 8), new Point(7, box.Y + 11), new Point(14, box.Y + 4) });
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(26, 0, Width - 26, Height), Enabled ? ForeColor : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    public class ChromeForm : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l);
        public readonly Panel Body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        readonly Panel titleBar;
        readonly Label titleLabel;
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                // Form.DoubleBuffered does not include native child controls; compose the whole editor to avoid partial frames.
                // This applies only to settings/dialog windows, never the layered crosshair overlay.
                parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return parameters;
            }
        }
        public ChromeForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            Font = new Font("Microsoft YaHei UI", 9.5F); ForeColor = Theme.Text; BackColor = Theme.Background;
            FormBorderStyle = FormBorderStyle.None; Padding = new Padding(1); StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true; MaximizeBox = false;
            titleBar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Card };
            var logo = new PictureBox { Bounds = new Rectangle(18, 12, 24, 24), Image = AppArtwork.CreateBitmap(24, true), SizeMode = PictureBoxSizeMode.Zoom };
            titleLabel = Theme.Label("屏幕准星", new Rectangle(52, 0, 300, 48));
            var close = Theme.Button("×", new Rectangle(0, 0, 48, 48)); close.Dock = DockStyle.Right;
            close.FlatAppearance.BorderSize = 0; close.BackColor = Theme.Card; close.Font = new Font(Font.FontFamily, 17F);
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(156, 52, 66); close.AccessibleName = "关闭窗口";
            close.Click += delegate { Close(); };
            var minimize = Theme.Button("−", new Rectangle(0, 0, 48, 48)); minimize.Dock = DockStyle.Right;
            minimize.FlatAppearance.BorderSize = 0; minimize.BackColor = Theme.Card; minimize.AccessibleName = "最小化";
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            MouseEventHandler drag = delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); } };
            titleBar.MouseDown += drag; titleLabel.MouseDown += drag; logo.MouseDown += drag;
            titleBar.Controls.Add(titleLabel); titleBar.Controls.Add(logo); titleBar.Controls.Add(minimize); titleBar.Controls.Add(close);
            Controls.Add(Body); Controls.Add(titleBar);
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (titleLabel != null) titleLabel.Text = Text; }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
        protected override void OnLoad(EventArgs e)
        {
            // Fit before the first visible frame so small work areas do not cause a second layout after showing.
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width)); Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            base.OnLoad(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && titleBar != null) foreach (Control control in titleBar.Controls) { var picture = control as PictureBox; if (picture != null) picture.Image.Dispose(); }
            base.Dispose(disposing);
        }
    }

    public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Theme.Text; base.OnRenderItemText(e); }
        sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Theme.Field; } }
            public override Color MenuItemBorder { get { return Theme.Border; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
        }
    }

    public static class AppArtwork
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static Bitmap CreateBitmap(int size, bool active)
        {
            var bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = size / 32F;
                using (var pen = new Pen(active ? Theme.Accent : Theme.Muted, Math.Max(1.5F, 2.8F * s)))
                {
                    g.DrawArc(pen, 5 * s, 5 * s, 22 * s, 22 * s, 15, 60); g.DrawArc(pen, 5 * s, 5 * s, 22 * s, 22 * s, 105, 60);
                    g.DrawArc(pen, 5 * s, 5 * s, 22 * s, 22 * s, 195, 60); g.DrawArc(pen, 5 * s, 5 * s, 22 * s, 22 * s, 285, 60);
                    g.DrawLine(pen, 16 * s, 1 * s, 16 * s, 10 * s); g.DrawLine(pen, 16 * s, 22 * s, 16 * s, 31 * s);
                    g.DrawLine(pen, 1 * s, 16 * s, 10 * s, 16 * s); g.DrawLine(pen, 22 * s, 16 * s, 31 * s, 16 * s);
                }
                if (active) using (var brush = new SolidBrush(Theme.Text)) g.FillEllipse(brush, 14 * s, 14 * s, 4 * s, 4 * s);
            }
            return bitmap;
        }
        public static Icon CreateIcon(int size, bool active)
        {
            using (Bitmap bitmap = CreateBitmap(size, active))
            {
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }

    public static class LayeredSurface
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref Point position, ref Size size, IntPtr src, ref Point origin, int key, ref Blend blend, int flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        public static void Present(IntPtr window, Point position, Bitmap bitmap)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero), handle = bitmap.GetHbitmap(Color.FromArgb(0)), previous = SelectObject(dc, handle);
            try
            {
                Size size = bitmap.Size; Point origin = Point.Empty; var blend = new Blend { Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(window, IntPtr.Zero, ref position, ref size, dc, ref origin, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { SelectObject(dc, previous); DeleteObject(handle); DeleteDC(dc); }
        }
    }
}

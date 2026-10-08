using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

[assembly: System.Reflection.AssemblyTitle("屏幕准星")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace ScreenCrosshair
{
    public static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    }

    public class Settings
    {
        public int Length = 8;
        public int Gap = 4;
        public int Thickness = 2;
        public int Shape = 0;
        public int ColorIndex = 0;
        public bool Outline = true;
        public bool CenterDot = false;
        public string Monitor = "";

        public static readonly Color[] Colors = { Color.FromArgb(80, 255, 100), Color.Cyan, Color.White, Color.FromArgb(255, 75, 90), Color.Yellow, Color.FromArgb(220, 110, 255) };
        public static readonly string[] ColorNames = { "荧光绿", "青色", "白色", "红色", "黄色", "紫色" };
        public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCrosshair", "settings.xml"); } }

        public void Validate()
        {
            Length = Math.Max(2, Math.Min(32, Length));
            Gap = Math.Max(0, Math.Min(20, Gap));
            Thickness = Math.Max(1, Math.Min(8, Thickness));
            Shape = Math.Max(0, Math.Min(2, Shape));
            ColorIndex = Math.Max(0, Math.Min(Colors.Length - 1, ColorIndex));
            Monitor = Monitor ?? "";
        }

        public static Settings Load(out string warning)
        {
            warning = null;
            try
            {
                if (!File.Exists(FilePath)) return new Settings();
                using (var input = File.OpenRead(FilePath))
                {
                    var result = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(input);
                    result.Validate();
                    return result;
                }
            }
            catch (Exception e)
            {
                warning = "设置读取失败，已使用默认准星。原设置文件保留。\n" + e.Message;
                return new Settings();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            // Atomic replacement prevents an interrupted save from corrupting the last working settings.
            using (var output = File.Create(temp)) new XmlSerializer(typeof(Settings)).Serialize(output, this);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
    }

    public static class Renderer
    {
        public static void Draw(Graphics g, Settings s, int cx, int cy)
        {
            // Solid pixel-aligned strokes avoid colored halos against a color-keyed transparent background.
            g.SmoothingMode = SmoothingMode.None;
            var blocks = new List<Rectangle>();
            int t = s.Thickness, d = s.Gap, n = s.Length;
            int half = t / 2;
            if (s.Shape == 0)
            {
                blocks.Add(new Rectangle(cx - half, cy - d - n, t, n));
                blocks.Add(new Rectangle(cx - half, cy + d, t, n));
                blocks.Add(new Rectangle(cx - d - n, cy - half, n, t));
                blocks.Add(new Rectangle(cx + d, cy - half, n, t));
            }
            if (s.Shape == 1 || s.CenterDot)
            {
                int size = s.Shape == 1 ? Math.Max(2, s.Thickness + 2) : s.Thickness;
                blocks.Add(new Rectangle(cx - size / 2, cy - size / 2, size, size));
            }
            using (var color = new SolidBrush(Settings.Colors[s.ColorIndex]))
            {
                if (s.Outline)
                    foreach (Rectangle r in blocks) { Rectangle edge = r; edge.Inflate(1, 1); g.FillRectangle(Brushes.Black, edge); }
                foreach (Rectangle r in blocks) g.FillRectangle(color, r);
                if (s.Shape == 2)
                {
                    int radius = s.Length;
                    var circle = new Rectangle(cx - radius, cy - radius, radius * 2, radius * 2);
                    if (s.Outline) using (var edge = new Pen(Color.Black, t + 2)) g.DrawEllipse(edge, circle);
                    using (var pen = new Pen(color, t)) g.DrawEllipse(pen, circle);
                }
            }
        }
    }

    public class Overlay : Form
    {
        public const string WindowTitle = "ScreenCrosshair.Overlay.v1";
        public readonly Settings Config;
        public readonly List<string> HotkeyErrors = new List<string>();
        readonly bool persist;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem toggle;
        readonly System.Windows.Forms.Timer timer;
        SettingsWindow settingsWindow;

        public Overlay(Settings config, bool persistence)
        {
            Config = config;
            persist = persistence;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(128, 128);
            Text = WindowTitle;
            BackColor = Color.FromArgb(1, 2, 3);
            TransparencyKey = BackColor;
            TopMost = true;
            DoubleBuffered = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var menu = new ContextMenuStrip();
            toggle = new ToolStripMenuItem("隐藏准星    Ctrl+Alt+F8", null, delegate { Toggle(); });
            menu.Items.Add(toggle);
            menu.Items.Add("调整准星…    Ctrl+Alt+F9", null, delegate { OpenSettings(); });
            menu.Items.Add("切换显示器    Ctrl+Alt+F10", null, delegate { CycleMonitor(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出    Ctrl+Alt+F11", null, delegate { Close(); });
            tray = new NotifyIcon { Icon = Icon, Text = "屏幕准星 · 双击调整", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { OpenSettings(); };

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += delegate { RefreshPosition(); };
            timer.Start();
            RefreshPosition();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // Layered + transparent passes even the painted strokes through to the underlying app.
                // NOACTIVATE keeps the game or editor focused when the overlay appears or moves.
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            HotkeyErrors.Clear();
            Keys[] keys = { Keys.F8, Keys.F9, Keys.F10, Keys.F11 };
            for (int i = 0; i < keys.Length; i++)
                if (!Native.RegisterHotKey(Handle, i + 1, 0x4003, (uint)keys[i])) HotkeyErrors.Add("Ctrl+Alt+" + keys[i]);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            for (int i = 1; i <= 4; i++) Native.UnregisterHotKey(Handle, i);
            base.OnHandleDestroyed(e);
        }

        public Screen SelectedScreen()
        {
            foreach (Screen screen in Screen.AllScreens) if (screen.DeviceName == Config.Monitor) return screen;
            return Screen.PrimaryScreen;
        }

        public void RefreshPosition()
        {
            Rectangle bounds = SelectedScreen().Bounds;
            // Full monitor bounds, including the taskbar area, give the actual display center.
            int x = bounds.Left + bounds.Width / 2 - Width / 2;
            int y = bounds.Top + bounds.Height / 2 - Height / 2;
            if (Left != x || Top != y) Location = new Point(x, y);
            if (IsHandleCreated && Visible) Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        }

        public void Toggle()
        {
            if (Visible) Hide(); else { Show(); RefreshPosition(); }
            toggle.Text = Visible ? "隐藏准星    Ctrl+Alt+F8" : "显示准星    Ctrl+Alt+F8";
        }

        public void CycleMonitor()
        {
            Screen[] screens = Screen.AllScreens;
            int current = Array.FindIndex(screens, delegate(Screen s) { return s.DeviceName == SelectedScreen().DeviceName; });
            Config.Monitor = screens[(current + 1) % screens.Length].DeviceName;
            Changed();
            if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.Reload();
        }

        public void Changed()
        {
            Config.Validate();
            RefreshPosition();
            Invalidate();
            if (!persist) return;
            try { Config.Save(); }
            catch (Exception e) { Notify("设置未保存", e.Message); }
        }

        public void Notify(string title, string message) { tray.ShowBalloonTip(4000, title, message, ToolTipIcon.Info); }

        public void OpenSettings()
        {
            if (settingsWindow == null || settingsWindow.IsDisposed) settingsWindow = new SettingsWindow(this);
            settingsWindow.Show();
            settingsWindow.WindowState = FormWindowState.Normal;
            settingsWindow.Activate();
        }

        protected override void OnPaint(PaintEventArgs e) { Renderer.Draw(e.Graphics, Config, ClientSize.Width / 2, ClientSize.Height / 2); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                switch (m.WParam.ToInt32()) { case 1: Toggle(); break; case 2: OpenSettings(); break; case 3: CycleMonitor(); break; case 4: Close(); break; }
                return;
            }
            if (m.Msg == 0x8001) { OpenSettings(); return; }
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
            if (m.Msg == 0x007E || m.Msg == 0x02E0) RefreshPosition();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop(); timer.Dispose();
            if (settingsWindow != null) settingsWindow.Dispose();
            tray.Visible = false;
            tray.ContextMenuStrip.Dispose(); tray.Dispose();
            base.OnFormClosed(e);
        }
    }

    public class Preview : Panel
    {
        public Settings Config;
        public Preview(Settings settings) { Config = settings; DoubleBuffered = true; BackColor = Color.FromArgb(24, 29, 38); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var grid = new Pen(Color.FromArgb(37, 44, 56)))
            {
                for (int x = Width / 2 % 24; x < Width; x += 24) e.Graphics.DrawLine(grid, x, 0, x, Height);
                for (int y = Height / 2 % 24; y < Height; y += 24) e.Graphics.DrawLine(grid, 0, y, Width, y);
            }
            Renderer.Draw(e.Graphics, Config, Width / 2, Height / 2);
        }
    }

    public class SettingsWindow : Form
    {
        readonly Overlay overlay;
        readonly Preview preview;
        readonly ComboBox shape = new ComboBox(), color = new ComboBox(), monitor = new ComboBox();
        readonly NumericUpDown length = new NumericUpDown(), gap = new NumericUpDown(), thickness = new NumericUpDown();
        readonly CheckBox outline = new CheckBox(), dot = new CheckBox();
        readonly Label status = new Label();
        bool loading;
        Screen[] screens;

        public SettingsWindow(Overlay owner)
        {
            overlay = owner;
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = "屏幕准星 · 调整";
            Icon = owner.Icon;
            ClientSize = new Size(584, 430);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 249, 252);
            var title = new Label { Text = "屏幕准星", Font = new Font(Font.FontFamily, 19F, FontStyle.Bold), Bounds = new Rectangle(24, 18, 280, 42) };
            Controls.Add(title);
            Controls.Add(new Label { Text = "始终置顶 · 鼠标穿透 · 调整即时生效", Bounds = new Rectangle(25, 65, 530, 26), ForeColor = Color.DimGray });
            preview = new Preview(owner.Config) { Bounds = new Rectangle(24, 105, 232, 198) };
            Controls.Add(preview);
            Controls.Add(new Label { Text = "实际像素预览", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray, Bounds = new Rectangle(24, 309, 232, 25) });
            AddRow("样式", shape, 105); shape.Items.AddRange(new object[] { "十字", "圆点", "圆环" });
            AddRow("颜色", color, 141); color.Items.AddRange(Settings.ColorNames);
            AddNumber("长度 / 半径", length, 177, 2, 32);
            AddNumber("中心间隔", gap, 213, 0, 20);
            AddNumber("线条粗细", thickness, 249, 1, 8);
            outline.Text = "黑色描边"; outline.SetBounds(288, 286, 116, 30);
            dot.Text = "中心点"; dot.SetBounds(428, 286, 120, 30);
            Controls.Add(outline); Controls.Add(dot);
            Controls.Add(new Label { Text = "显示器", Bounds = new Rectangle(24, 346, 65, 25) });
            monitor.SetBounds(92, 343, 272, 29); monitor.DropDownStyle = ComboBoxStyle.DropDownList; Controls.Add(monitor);
            var reset = new Button { Text = "恢复默认", Bounds = new Rectangle(384, 343, 94, 30) };
            reset.Click += delegate
            {
                Settings s = owner.Config;
                s.Length = 8; s.Gap = 4; s.Thickness = 2; s.Shape = 0; s.ColorIndex = 0; s.Outline = true; s.CenterDot = false;
                Reload(); overlay.Changed();
            };
            Controls.Add(reset);
            var done = new Button { Text = "完成", Bounds = new Rectangle(486, 343, 74, 30) };
            done.Click += delegate { Close(); }; Controls.Add(done);
            status.SetBounds(24, 388, 542, 30); status.Font = new Font(Font.FontFamily, 8.5F); status.ForeColor = Color.DimGray; Controls.Add(status);
            shape.SelectedIndexChanged += UpdateSettings; color.SelectedIndexChanged += UpdateSettings; monitor.SelectedIndexChanged += UpdateSettings;
            length.ValueChanged += UpdateSettings; gap.ValueChanged += UpdateSettings; thickness.ValueChanged += UpdateSettings;
            outline.CheckedChanged += UpdateSettings; dot.CheckedChanged += UpdateSettings;
            Reload();
        }

        void AddRow(string text, ComboBox control, int y)
        {
            Controls.Add(new Label { Text = text, Bounds = new Rectangle(280, y + 3, 112, 26) });
            control.SetBounds(397, y, 163, 29); control.DropDownStyle = ComboBoxStyle.DropDownList; Controls.Add(control);
        }

        void AddNumber(string text, NumericUpDown control, int y, int min, int max)
        {
            Controls.Add(new Label { Text = text, Bounds = new Rectangle(280, y + 3, 112, 26) });
            control.SetBounds(397, y, 163, 29); control.Minimum = min; control.Maximum = max; Controls.Add(control);
        }

        public void Reload()
        {
            loading = true;
            Settings s = overlay.Config;
            shape.SelectedIndex = s.Shape; color.SelectedIndex = s.ColorIndex;
            length.Value = s.Length; gap.Value = s.Gap; thickness.Value = s.Thickness;
            outline.Checked = s.Outline; dot.Checked = s.CenterDot;
            screens = Screen.AllScreens; monitor.Items.Clear();
            foreach (Screen screen in screens) monitor.Items.Add(screen.DeviceName + (screen.Primary ? "（主屏）" : "") + " " + screen.Bounds.Width + "×" + screen.Bounds.Height);
            monitor.SelectedIndex = Array.FindIndex(screens, delegate(Screen screen) { return screen.DeviceName == overlay.SelectedScreen().DeviceName; });
            status.Text = overlay.HotkeyErrors.Count == 0 ? "Ctrl+Alt+F8 显示/隐藏    ·    F9 调整    ·    F10 换屏    ·    F11 退出" : "快捷键被占用：" + string.Join("、", overlay.HotkeyErrors.ToArray()) + "；请使用托盘菜单。";
            loading = false;
            EnableFields(); preview.Invalidate();
        }

        void EnableFields() { length.Enabled = shape.SelectedIndex != 1; gap.Enabled = shape.SelectedIndex == 0; dot.Enabled = shape.SelectedIndex != 1; }
        void UpdateSettings(object sender, EventArgs args)
        {
            if (loading) return;
            Settings s = overlay.Config;
            s.Shape = shape.SelectedIndex; s.ColorIndex = color.SelectedIndex;
            s.Length = (int)length.Value; s.Gap = (int)gap.Value; s.Thickness = (int)thickness.Value;
            s.Outline = outline.Checked; s.CenterDot = dot.Checked;
            if (monitor.SelectedIndex >= 0) s.Monitor = screens[monitor.SelectedIndex].DeviceName;
            EnableFields(); overlay.Changed(); preview.Invalidate();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool first;
            using (var mutex = new Mutex(true, @"Local\ScreenCrosshair.v1", out first))
            {
                if (!first)
                {
                    IntPtr existing = Native.FindWindow(null, Overlay.WindowTitle);
                    if (existing != IntPtr.Zero) Native.PostMessage(existing, 0x8001, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string warning;
                var overlay = new Overlay(Settings.Load(out warning), true);
                overlay.Shown += delegate
                {
                    if (warning != null) overlay.Notify("屏幕准星", warning);
                    else if (overlay.HotkeyErrors.Count > 0) overlay.Notify("部分快捷键被占用", string.Join("、", overlay.HotkeyErrors.ToArray()) + "。请右击托盘准星图标操作。");
                    else overlay.Notify("准星已开启", "Ctrl+Alt+F8 显示/隐藏，Ctrl+Alt+F9 调整。右击托盘图标可退出。");
                };
                Application.Run(overlay);
                mutex.ReleaseMutex();
            }
        }
    }
}

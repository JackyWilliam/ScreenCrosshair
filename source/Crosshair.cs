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
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]

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

    public class CrosshairStyle
    {
        public int Length = 8;
        public int Gap = 4;
        public int Thickness = 2;
        public int Shape = 0;
        public int ColorIndex = 0;
        public bool Outline = true;
        public bool CenterDot = false;
        public string ColorHex = "";

        [XmlIgnore] public Color ForegroundColor
        {
            get { Color custom; return Settings.TryParseColor(ColorHex, out custom) ? custom : Settings.Colors[ColorIndex]; }
        }

        public CrosshairStyle Copy(int shape)
        {
            return new CrosshairStyle { Length = Length, Gap = Gap, Thickness = Thickness, Shape = shape, ColorIndex = ColorIndex, ColorHex = ColorHex, Outline = Outline, CenterDot = CenterDot };
        }

        public void ValidateAppearance()
        {
            Length = Math.Max(2, Math.Min(32, Length)); Gap = Math.Max(0, Math.Min(20, Gap)); Thickness = Math.Max(1, Math.Min(8, Thickness));
            Shape = Math.Max(0, Math.Min(4, Shape)); ColorIndex = Math.Max(0, Math.Min(Settings.Colors.Length - 1, ColorIndex));
            Color custom; ColorHex = Settings.TryParseColor(ColorHex, out custom) ? Settings.ToHex(custom) : "";
        }
    }

    public class Settings : CrosshairStyle
    {
        // Keep ordinary appearance fields at the XML root so existing settings migrate without losing values.
        // Missing class profiles copy the old shared appearance once; subsequent edits stay independent.
        public CrosshairStyle Shotgun;
        public CrosshairStyle Sniper;
        public string Monitor = "";
        public bool AutoDetect = false;
        public string OcrLanguage = "zh-Hans-CN";
        public int ScanInterval = 1000;
        public bool CaptureRegionSet = false;
        public int CaptureLayoutVersion = 0;
        public double CaptureX, CaptureY, CaptureWidth, CaptureHeight;

        public CrosshairStyle Profile(WeaponKind kind)
        {
            if (Shotgun == null) Shotgun = Copy(3);
            if (Sniper == null) Sniper = Copy(4);
            return kind == WeaponKind.Shotgun ? Shotgun : kind == WeaponKind.Sniper ? Sniper : this;
        }

        public void ResetProfile(WeaponKind kind)
        {
            CrosshairStyle current = Profile(kind);
            var defaults = new CrosshairStyle();
            current.Length = defaults.Length; current.Gap = defaults.Gap; current.Thickness = defaults.Thickness;
            current.Shape = kind == WeaponKind.Shotgun ? 3 : kind == WeaponKind.Sniper ? 4 : 0;
            current.ColorIndex = 0; current.ColorHex = ""; current.Outline = true; current.CenterDot = false;
        }

        public static bool TryParseColor(string text, out Color color)
        {
            int value; string hex = (text ?? "").Trim().TrimStart('#');
            if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out value))
            { color = Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255); return true; }
            color = Color.Empty; return false;
        }

        public static string ToHex(Color color) { return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2"); }

        public static readonly Color[] Colors = { Color.FromArgb(80, 255, 100), Color.Cyan, Color.White, Color.FromArgb(255, 75, 90), Color.Yellow, Color.FromArgb(220, 110, 255) };
        public static readonly string[] ColorNames = { "荧光绿", "青色", "白色", "红色", "黄色", "紫色" };
        public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCrosshair", "settings.xml"); } }

        public void Validate()
        {
            ValidateAppearance();
            Profile(WeaponKind.Shotgun).ValidateAppearance(); Profile(WeaponKind.Sniper).ValidateAppearance();
            Monitor = Monitor ?? "";
            if (OcrLanguage != "en-US" && OcrLanguage != "zh-Hans-CN") OcrLanguage = "zh-Hans-CN";
            if (ScanInterval != 500 && ScanInterval != 1000 && ScanInterval != 2000) ScanInterval = 1000;
            if (!(CaptureX >= 0 && CaptureY >= 0 && CaptureWidth > 0 && CaptureHeight > 0 && CaptureX + CaptureWidth <= 1.001 && CaptureY + CaptureHeight <= 1.001)) CaptureRegionSet = false;
            // v1.1.0 asked users to crop a single slot, which cannot follow a switch to the other slot.
            if (CaptureLayoutVersion < 2) CaptureRegionSet = false;
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
            Validate();
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
        public static void Draw(Graphics g, CrosshairStyle s, int cx, int cy)
        { Draw(g, s, cx, cy, s.Shape); }

        public static void Draw(Graphics g, CrosshairStyle s, int cx, int cy, int shape)
        {
            // Solid pixel-aligned strokes avoid colored halos against a color-keyed transparent background.
            g.SmoothingMode = SmoothingMode.None;
            var blocks = new List<Rectangle>();
            int t = s.Thickness, d = s.Gap, n = s.Length;
            int half = t / 2;
            if (shape == 0 || shape == 4)
            {
                blocks.Add(new Rectangle(cx - half, cy - d - n, t, n));
                if (shape != 4) blocks.Add(new Rectangle(cx - half, cy + d, t, n));
                blocks.Add(new Rectangle(cx - d - n, cy - half, n, t));
                blocks.Add(new Rectangle(cx + d, cy - half, n, t));
            }
            if (shape == 1 || shape == 3 || s.CenterDot)
            {
                int size = shape == 1 ? Math.Max(2, s.Thickness + 2) : s.Thickness;
                blocks.Add(new Rectangle(cx - size / 2, cy - size / 2, size, size));
            }
            using (var color = new SolidBrush(s.ForegroundColor))
            {
                if (s.Outline)
                    foreach (Rectangle r in blocks) { Rectangle edge = r; edge.Inflate(1, 1); g.FillRectangle(Brushes.Black, edge); }
                foreach (Rectangle r in blocks) g.FillRectangle(color, r);
                if (shape == 2 || shape == 3)
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
        readonly System.Windows.Forms.Timer saveTimer;
        bool dirty;
        public readonly RecognitionController Recognition;
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
            ApplyTransparencyColor();
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
            saveTimer = new System.Windows.Forms.Timer { Interval = 350 };
            saveTimer.Tick += delegate { SaveNow(); };
            Recognition = new RecognitionController(Config);
            Recognition.Updated += delegate { Invalidate(); };
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
            Changed(false);
            if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.Reload();
        }

        public void Changed(bool recognitionSettingsChanged = true)
        {
            Config.Validate();
            ApplyTransparencyColor();
            if (recognitionSettingsChanged) Recognition.Reset();
            RefreshPosition();
            Invalidate();
            if (!persist) return;
            // Slider drags repaint immediately; debounce disk writes until the user pauses.
            dirty = true; saveTimer.Stop(); saveTimer.Start();
        }

        void ApplyTransparencyColor()
        {
            // Reserve a key unused by all profiles, so a recognition switch cannot make a custom color disappear.
            Color key = Color.FromArgb(1, 2, 3);
            for (int i = 1; i <= 4; i++)
            {
                key = Color.FromArgb(i, i + 1, i + 2);
                if (key.ToArgb() != Config.ForegroundColor.ToArgb() && key.ToArgb() != Config.Profile(WeaponKind.Shotgun).ForegroundColor.ToArgb() && key.ToArgb() != Config.Profile(WeaponKind.Sniper).ForegroundColor.ToArgb()) break;
            }
            if (BackColor != key) { BackColor = key; TransparencyKey = key; }
        }

        void SaveNow()
        {
            saveTimer.Stop();
            if (!dirty) return;
            dirty = false;
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

        protected override void OnPaint(PaintEventArgs e) { Renderer.Draw(e.Graphics, Recognition == null ? Config : Recognition.DisplayStyle, ClientSize.Width / 2, ClientSize.Height / 2); }

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
            Recognition.Dispose(); SaveNow(); saveTimer.Dispose();
            if (settingsWindow != null) settingsWindow.Dispose();
            tray.Visible = false;
            tray.ContextMenuStrip.Dispose(); tray.Dispose();
            base.OnFormClosed(e);
        }
    }

    public class Preview : Panel
    {
        public CrosshairStyle Config;
        public Preview(CrosshairStyle settings) { Config = settings; DoubleBuffered = true; BackColor = Color.FromArgb(24, 29, 38); }
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

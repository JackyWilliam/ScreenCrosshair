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
[assembly: System.Reflection.AssemblyVersion("1.3.6.0")]

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

    public static class CrosshairParts
    {
        public const int Top = 1, Bottom = 2, Left = 4, Right = 8, Ring = 16;
        public const int BottomLeft = 32, BottomRight = 64, TopLeft = 128, TopRight = 256;
        public const int Cross = Top | Bottom | Left | Right;
        public const int Diagonals = BottomLeft | BottomRight | TopLeft | TopRight;
        public const int All = Cross | Ring | Diagonals;
    }

    public class CrosshairStyle
    {
        public double Length = 8;
        public double Gap = 4;
        public double Thickness = 2;
        public int Shape = 0;
        public int ColorIndex = 0;
        public bool Outline = true;
        public bool CenterDot = false;
        public string ColorHex = "";
        public int CustomParts = CrosshairParts.Cross;
        public double DiagonalAngle = 45;

        public int PartsForShape(int shape)
        {
            if (shape == 5) return CustomParts;
            if (shape == 6) return CrosshairParts.Top | CrosshairParts.BottomLeft | CrosshairParts.BottomRight;
            if (shape == 7) return CrosshairParts.Diagonals;
            if (shape == 4) return CrosshairParts.Top | CrosshairParts.Left | CrosshairParts.Right;
            if (shape == 2 || shape == 3) return CrosshairParts.Ring;
            return shape == 0 ? CrosshairParts.Cross : 0;
        }
        public double AngleForShape(int shape) { return shape == 6 ? 30 : shape == 5 ? DiagonalAngle : 45; }

        [XmlIgnore] public Color ForegroundColor
        {
            get { Color custom; return Settings.TryParseColor(ColorHex, out custom) ? custom : Settings.Colors[ColorIndex]; }
        }

        public CrosshairStyle Copy(int shape)
        {
            return new CrosshairStyle { Length = Length, Gap = Gap, Thickness = Thickness, Shape = shape, ColorIndex = ColorIndex, ColorHex = ColorHex, Outline = Outline, CenterDot = CenterDot, CustomParts = CustomParts, DiagonalAngle = DiagonalAngle };
        }

        public void Apply(CrosshairStyle source)
        {
            Length = source.Length; Gap = source.Gap; Thickness = source.Thickness; Shape = source.Shape;
            ColorIndex = source.ColorIndex; ColorHex = source.ColorHex; Outline = source.Outline; CenterDot = source.CenterDot;
            CustomParts = source.CustomParts; DiagonalAngle = source.DiagonalAngle;
        }

        public bool SameAppearance(CrosshairStyle other)
        {
            return other != null && Length == other.Length && Gap == other.Gap && Thickness == other.Thickness && Shape == other.Shape
                && ColorIndex == other.ColorIndex && ColorHex == other.ColorHex && Outline == other.Outline && CenterDot == other.CenterDot
                && CustomParts == other.CustomParts && DiagonalAngle == other.DiagonalAngle;
        }

        static double Dimension(double value, double min, double max, double fallback)
        { return double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Round(Math.Max(min, Math.Min(max, value)), 2); }

        public void ValidateAppearance()
        {
            Length = Dimension(Length, 2, 32, 8); Gap = Dimension(Gap, 0, 20, 4); Thickness = Dimension(Thickness, 0.1, 8, 2);
            Shape = Math.Max(0, Math.Min(7, Shape)); ColorIndex = Math.Max(0, Math.Min(Settings.Colors.Length - 1, ColorIndex));
            CustomParts &= CrosshairParts.All; DiagonalAngle = Dimension(DiagonalAngle, 5, 85, 45);
            Color custom; ColorHex = Settings.TryParseColor(ColorHex, out custom) ? Settings.ToHex(custom) : "";
        }
    }

    public class CustomPreset
    {
        public string Name = "";
        public CrosshairStyle Style = new CrosshairStyle();
        public override string ToString() { return Name; }
    }

    public class Settings : CrosshairStyle
    {
        // Keep ordinary appearance fields at the XML root so existing settings migrate without losing values.
        // Missing class profiles copy the old shared appearance once; subsequent edits stay independent.
        public CrosshairStyle Shotgun;
        public CrosshairStyle Sniper;
        public List<CustomPreset> CustomPresets = new List<CustomPreset>();
        public List<CustomPreset> ShotgunPresets = new List<CustomPreset>();
        public List<CustomPreset> SniperPresets = new List<CustomPreset>();
        public int PresetLayoutVersion;
        public string Monitor = "";
        public bool AutoDetect = false;
        public string OcrLanguage = "zh-Hans-CN";
        public int ScanInterval = 1000;
        public bool CaptureRegionSet = false;
        public int CaptureLayoutVersion = 0;
        public double CaptureX, CaptureY, CaptureWidth, CaptureHeight;

        public static string ProfileName(WeaponKind kind)
        { return kind == WeaponKind.Shotgun ? "霰弹枪" : kind == WeaponKind.Sniper ? "狙击枪" : "普通（十字）"; }

        public List<CustomPreset> Presets(WeaponKind kind)
        {
            if (CustomPresets == null) CustomPresets = new List<CustomPreset>();
            if (PresetLayoutVersion < 1)
            {
                // Old presets were usable in every category. Copy once so upgrading preserves that access,
                // then each list owns its snapshots; an intentionally emptied list must stay empty on reload.
                ShotgunPresets = CopyPresets(CustomPresets); SniperPresets = CopyPresets(CustomPresets);
                PresetLayoutVersion = 1;
            }
            if (ShotgunPresets == null) ShotgunPresets = new List<CustomPreset>();
            if (SniperPresets == null) SniperPresets = new List<CustomPreset>();
            return kind == WeaponKind.Shotgun ? ShotgunPresets : kind == WeaponKind.Sniper ? SniperPresets : CustomPresets;
        }
        static List<CustomPreset> CopyPresets(List<CustomPreset> source)
        {
            var copies = new List<CustomPreset>();
            foreach (CustomPreset preset in source)
                if (preset != null && preset.Style != null) copies.Add(new CustomPreset { Name = preset.Name, Style = preset.Style.Copy(preset.Style.Shape) });
            return copies;
        }

        public CustomPreset SavePreset(string name, CrosshairStyle style, WeaponKind kind = WeaponKind.Ordinary)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 40 || Array.Exists(name.ToCharArray(), char.IsControl))
                throw new ArgumentException("名称需为 1–40 个可见字符。");
            List<CustomPreset> presets = Presets(kind);
            CustomPreset preset = presets.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (preset == null) { preset = new CustomPreset { Name = name }; presets.Add(preset); }
            // Store snapshots: editing a weapon profile must never mutate a saved preset, or vice versa.
            preset.Style = style.Copy(style.Shape); preset.Style.ValidateAppearance();
            return preset;
        }

        public CrosshairStyle Profile(WeaponKind kind)
        {
            if (Shotgun == null) Shotgun = Copy(3);
            if (Sniper == null) Sniper = Copy(4);
            return kind == WeaponKind.Shotgun ? Shotgun : kind == WeaponKind.Sniper ? Sniper : this;
        }

        public void SyncCommonParameters(WeaponKind sourceKind)
        {
            CrosshairStyle source = Profile(sourceKind);
            foreach (WeaponKind kind in new[] { WeaponKind.Ordinary, WeaponKind.Shotgun, WeaponKind.Sniper })
            {
                if (kind == sourceKind) continue;
                CrosshairStyle target = Profile(kind);
                // Sync is a one-time copy of shared appearance only. Shape, component switches and
                // angles belong to each category; saved preset snapshots must not change with it.
                target.Length = source.Length; target.Gap = source.Gap; target.Thickness = source.Thickness;
                target.ColorIndex = source.ColorIndex; target.ColorHex = source.ColorHex; target.Outline = source.Outline;
            }
        }

        public void ResetProfile(WeaponKind kind)
        {
            CrosshairStyle current = Profile(kind);
            var defaults = new CrosshairStyle();
            current.Length = defaults.Length; current.Gap = defaults.Gap; current.Thickness = defaults.Thickness;
            current.Shape = kind == WeaponKind.Shotgun ? 3 : kind == WeaponKind.Sniper ? 4 : 0;
            current.ColorIndex = 0; current.ColorHex = ""; current.Outline = true; current.CenterDot = false;
            current.CustomParts = defaults.CustomParts; current.DiagonalAngle = defaults.DiagonalAngle;
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
            foreach (WeaponKind kind in new[] { WeaponKind.Ordinary, WeaponKind.Shotgun, WeaponKind.Sniper })
            {
                List<CustomPreset> presets = Presets(kind);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                presets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name) || p.Style == null || !names.Add(p.Name.Trim()));
                foreach (CustomPreset preset in presets) { preset.Name = preset.Name.Trim(); preset.Style.ValidateAppearance(); }
            }
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
            g.SmoothingMode = SmoothingMode.None;
            var blocks = new List<RectangleF>();
            float t = (float)s.Thickness, d = (float)s.Gap, n = (float)s.Length;
            // Keep old integer strokes aligned; fractional dimensions are resolved by alpha coverage in Rasterize.
            float half = t == (int)t ? (int)t / 2 : t / 2;
            int parts = s.PartsForShape(shape);
            if ((parts & CrosshairParts.Top) != 0) blocks.Add(new RectangleF(cx - half, cy - d - n, t, n));
            if ((parts & CrosshairParts.Bottom) != 0) blocks.Add(new RectangleF(cx - half, cy + d, t, n));
            if ((parts & CrosshairParts.Left) != 0) blocks.Add(new RectangleF(cx - d - n, cy - half, n, t));
            if ((parts & CrosshairParts.Right) != 0) blocks.Add(new RectangleF(cx + d, cy - half, n, t));
            if (shape == 1 || shape == 3 || s.CenterDot)
            {
                float size = shape == 1 ? Math.Max(2, t + 2) : t;
                float offset = size == (int)size ? (int)size / 2 : size / 2;
                blocks.Add(new RectangleF(cx - offset, cy - offset, size, size));
            }
            using (var color = new SolidBrush(s.ForegroundColor))
            {
                if (s.Outline)
                    foreach (RectangleF r in blocks) { RectangleF edge = r; edge.Inflate(1, 1); g.FillRectangle(Brushes.Black, edge); }
                foreach (RectangleF r in blocks) g.FillRectangle(color, r);
                if ((parts & CrosshairParts.Diagonals) != 0)
                {
                    double radians = s.AngleForShape(shape) * Math.PI / 180;
                    float dx = (float)Math.Cos(radians), dy = (float)Math.Sin(radians);
                    if ((parts & CrosshairParts.BottomLeft) != 0) Ray(g, color, s.Outline, cx, cy, -dx, dy, d, n, t);
                    if ((parts & CrosshairParts.BottomRight) != 0) Ray(g, color, s.Outline, cx, cy, dx, dy, d, n, t);
                    if ((parts & CrosshairParts.TopLeft) != 0) Ray(g, color, s.Outline, cx, cy, -dx, -dy, d, n, t);
                    if ((parts & CrosshairParts.TopRight) != 0) Ray(g, color, s.Outline, cx, cy, dx, -dy, d, n, t);
                }
                if ((parts & CrosshairParts.Ring) != 0)
                {
                    float radius = n + ((parts & (CrosshairParts.Cross | CrosshairParts.Diagonals)) != 0 ? d : 0);
                    var circle = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
                    if (s.Outline) using (var edge = new Pen(Color.Black, t + 2)) g.DrawEllipse(edge, circle);
                    using (var pen = new Pen(color, t)) g.DrawEllipse(pen, circle);
                }
            }
        }

        static void Ray(Graphics g, Brush color, bool outline, float cx, float cy, float dx, float dy, float gap, float length, float thickness)
        {
            float px = -dy * thickness / 2, py = dx * thickness / 2;
            var points = new[] { new PointF(cx + dx * gap + px, cy + dy * gap + py), new PointF(cx + dx * (gap + length) + px, cy + dy * (gap + length) + py),
                new PointF(cx + dx * (gap + length) - px, cy + dy * (gap + length) - py), new PointF(cx + dx * gap - px, cy + dy * gap - py) };
            if (outline) using (var pen = new Pen(Color.Black, 2)) { pen.LineJoin = LineJoin.Miter; g.DrawPolygon(pen, points); }
            g.FillPolygon(color, points);
        }

        public static Bitmap Rasterize(CrosshairStyle style, int width, int height)
        {
            var result = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            bool fractional = style.Length % 1 != 0 || style.Gap % 1 != 0 || style.Thickness % 1 != 0 || (style.PartsForShape(style.Shape) & CrosshairParts.Diagonals) != 0;
            using (Graphics target = Graphics.FromImage(result))
            {
                if (!fractional) Draw(target, style, width / 2, height / 2);
                else
                {
                    // Supersampling preserves subpixel coverage without a color-key fringe on the desktop.
                    using (var large = new Bitmap(width * 8, height * 8, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                    {
                        using (Graphics g = Graphics.FromImage(large)) { g.ScaleTransform(8, 8); Draw(g, style, width / 2, height / 2); }
                        target.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        target.PixelOffsetMode = PixelOffsetMode.Half;
                        target.DrawImage(large, new Rectangle(0, 0, width, height));
                    }
                }
            }
            return result;
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
        CrosshairStyle renderedStyle;
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
            TopMost = true;
            DoubleBuffered = true;
            Icon = AppArtwork.CreateIcon(32, true);

            var menu = new ContextMenuStrip { BackColor = Theme.Card, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = new Font("Microsoft YaHei UI", 9F) };
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
            Recognition.Updated += delegate { RefreshSurface(); };
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
            renderedStyle = null;
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
            Icon previous = tray.Icon;
            tray.Icon = AppArtwork.CreateIcon(32, Visible);
            if (!ReferenceEquals(previous, Icon)) previous.Dispose();
            tray.Text = Visible ? "屏幕准星 · 已开启 · 双击调整" : "屏幕准星 · 已隐藏 · 双击调整";
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
            if (recognitionSettingsChanged) Recognition.Reset();
            RefreshPosition();
            RefreshSurface();
            if (!persist) return;
            // Slider drags repaint immediately; debounce disk writes until the user pauses.
            dirty = true; saveTimer.Stop(); saveTimer.Start();
        }

        void RefreshSurface()
        {
            if (!IsHandleCreated || !Visible) { renderedStyle = null; return; }
            CrosshairStyle style = Recognition == null ? Config : Recognition.DisplayStyle;
            // Status updates arrive on every OCR sample; only changed geometry/color needs a new bitmap upload.
            if (style.SameAppearance(renderedStyle)) return;
            using (Bitmap bitmap = Renderer.Rasterize(style, Width, Height))
                LayeredSurface.Present(Handle, Location, bitmap);
            renderedStyle = style.Copy(style.Shape);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); RefreshSurface(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); RefreshSurface(); }

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

        protected override void OnPaint(PaintEventArgs e) { RefreshSurface(); }

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
            if (!ReferenceEquals(tray.Icon, Icon)) tray.Icon.Dispose();
            tray.ContextMenuStrip.Dispose(); tray.Dispose();
            Icon.Dispose();
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
            using (Bitmap image = Renderer.Rasterize(Config, 128, 128)) e.Graphics.DrawImageUnscaled(image, Width / 2 - 64, Height / 2 - 64);
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

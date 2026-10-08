using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenCrosshair
{
    public enum WeaponKind { Ordinary, Shotgun, Sniper }
    public sealed class Weapon
    {
        public readonly string Name;
        public readonly WeaponKind Kind;
        public readonly string[] Aliases;
        public Weapon(string name, WeaponKind kind, params string[] aliases) { Name = name; Kind = kind; Aliases = aliases; }
    }

    public static class WeaponCatalog
    {
        // Explicit names avoid mistaking generic "sniper ammo" for a gun; activation is resolved from label brightness.
        // These are overlay profiles, not game weapon classes: the user groups marksman weapons with snipers.
        public static readonly Weapon[] All = {
            new Weapon("和平捍卫者", WeaponKind.Shotgun, "PEACEKEEPER", "和平捍卫者", "和平捍衛者", "和平使者"),
            new Weapon("敖犬", WeaponKind.Shotgun, "MASTIFF", "敖犬", "獒犬"),
            new Weapon("EVA-8", WeaponKind.Shotgun, "EVA8"),
            new Weapon("莫桑比克", WeaponKind.Shotgun, "MOZAMBIQUE", "莫桑比克", "莫三比克"),
            new Weapon("哨兵", WeaponKind.Sniper, "SENTINEL", "哨兵"),
            new Weapon("长弓", WeaponKind.Sniper, "LONGBOW", "长弓", "長弓"),
            new Weapon("克雷贝尔", WeaponKind.Sniper, "KRABER", "克雷贝尔", "克雷貝爾", "克萊博"),
            new Weapon("充能步枪", WeaponKind.Sniper, "CHARGERIFLE", "充能步枪", "充能步槍", "充能狙击", "充能狙擊"),
            new Weapon("R-301", WeaponKind.Ordinary, "R301"), new Weapon("R-99", WeaponKind.Ordinary, "R99"),
            new Weapon("平行步枪", WeaponKind.Ordinary, "FLATLINE", "平行步枪", "平行步槍", "VK47"),
            new Weapon("哈沃克", WeaponKind.Ordinary, "HAVOC", "哈沃克", "哈博克"),
            new Weapon("赫姆洛克", WeaponKind.Ordinary, "HEMLOK", "赫姆洛克", "汗洛"),
            new Weapon("复仇女神", WeaponKind.Ordinary, "NEMESIS", "复仇女神", "復仇女神", "復仇者"),
            new Weapon("转换者", WeaponKind.Ordinary, "ALTERNATOR", "转换者", "轉換者"),
            new Weapon("猎兽", WeaponKind.Ordinary, "PROWLER", "猎兽", "獵獸"),
            new Weapon("电能", WeaponKind.Ordinary, "VOLT", "电能", "電能"), new Weapon("C.A.R.", WeaponKind.Ordinary, "CAR"),
            new Weapon("专注", WeaponKind.Ordinary, "DEVOTION", "专注", "專注"), new Weapon("L-STAR", WeaponKind.Ordinary, "LSTAR"),
            new Weapon("喷火", WeaponKind.Ordinary, "SPITFIRE", "喷火", "噴火", "M600"),
            new Weapon("暴走", WeaponKind.Ordinary, "RAMPAGE", "暴走", "狂暴"),
            new Weapon("G7", WeaponKind.Sniper, "G7SCOUT", "G7侦察", "G7偵察", "G7"),
            new Weapon("三重式", WeaponKind.Sniper, "TRIPLETAKE", "三重式", "三重击", "三重擊"),
            new Weapon("30-30", WeaponKind.Sniper, "3030"), new Weapon("波塞克", WeaponKind.Sniper, "BOCEK", "波塞克", "博切克"),
            new Weapon("RE-45", WeaponKind.Ordinary, "RE45"), new Weapon("P2020", WeaponKind.Ordinary, "P2020"),
            new Weapon("辅助手枪", WeaponKind.Ordinary, "WINGMAN", "辅助手枪", "輔助手槍")
        };
        public static string Normalize(string text)
        {
            var result = new StringBuilder();
            foreach (char c in (text ?? "").ToUpperInvariant()) if (char.IsLetterOrDigit(c)) result.Append(c);
            return result.ToString();
        }
        public static Weapon Match(string text)
        {
            string normalized = Normalize(text);
            Weapon match = null;
            foreach (Weapon weapon in All)
                foreach (string alias in weapon.Aliases)
                    if (alias == "CAR" ? (normalized == "CAR" || normalized.StartsWith("CARSMG") || normalized.StartsWith("CAR冲锋") || normalized.StartsWith("CAR衝鋒")) : normalized.Contains(Normalize(alias)))
                    {
                        if (match != null && match != weapon) return null;
                        match = weapon; break;
                    }
            return match;
        }
    }

    public sealed class StableWeaponTracker
    {
        string candidate;
        int repeats, misses;
        public Weapon Current { get; private set; }
        public void Reset() { candidate = null; repeats = misses = 0; Current = null; }
        public Weapon Observe(Weapon next)
        {
            if (next == null)
            {
                candidate = null; repeats = 0;
                if (++misses >= 3) Current = null;
            }
            else
            {
                misses = 0;
                repeats = candidate == next.Name ? repeats + 1 : 1;
                candidate = next.Name;
                if (repeats >= 2) Current = next;
            }
            return Current;
        }
    }

    public static class ApexWindow
    {
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        public static bool Foreground(out IntPtr handle, out Rectangle bounds)
        {
            handle = Native.GetForegroundWindow(); bounds = Rectangle.Empty;
            if (handle == IntPtr.Zero || IsIconic(handle)) return false;
            uint pid; GetWindowThreadProcessId(handle, out pid);
#if CROSSHAIR_SELF_TEST
            // A test named r5apex must never capture a real Apex session if focus changes during the test.
            if (pid != (uint)Process.GetCurrentProcess().Id) return false;
#endif
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                    if (process.ProcessName != "r5apex" && process.ProcessName != "r5apex_dx12") return false;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
            Rect client; Point origin = Point.Empty;
            if (!GetClientRect(handle, out client) || !ClientToScreen(handle, ref origin)) return false;
            bounds = new Rectangle(origin, new Size(client.Right - client.Left, client.Bottom - client.Top));
            return bounds.Width > 100 && bounds.Height > 100;
        }
        public static Rectangle CaptureBounds(Settings settings, Rectangle client)
        {
            return Rectangle.Intersect(client, new Rectangle(client.Left + (int)(settings.CaptureX * client.Width), client.Top + (int)(settings.CaptureY * client.Height), Math.Max(1, (int)(settings.CaptureWidth * client.Width)), Math.Max(1, (int)(settings.CaptureHeight * client.Height))));
        }
    }

    public sealed class RecognitionController : IDisposable
    {
        readonly Settings config;
        readonly Timer timer;
        readonly StableWeaponTracker tracker = new StableWeaponTracker();
        WindowsOcr ocr;
        bool busy, disposed, failed;
        int generation;
        IntPtr lastWindow;
        Rectangle lastBounds;
        readonly Queue<string> history = new Queue<string>();
        string lastEvent;
        public string Status { get; private set; }
        public string LastResult { get; private set; }
        public string LastRecognizedText { get; private set; }
        public string HistoryText { get { return string.Join(Environment.NewLine, history.ToArray()); } }
        public double LastMilliseconds { get; private set; }
        public event Action Updated;
        public CrosshairStyle DisplayStyle { get { return config.Profile(tracker.Current == null ? WeaponKind.Ordinary : tracker.Current.Kind); } }
        public int DisplayShape { get { return DisplayStyle.Shape; } }

        public RecognitionController(Settings settings)
        {
            config = settings;
            timer = new Timer { Interval = settings.ScanInterval };
            timer.Tick += Tick;
            timer.Start(); Reset();
        }
        public void Reset()
        {
            generation++; failed = false; tracker.Reset(); lastWindow = IntPtr.Zero;
            timer.Interval = config.ScanInterval;
            SetStatus(!config.AutoDetect ? "自动识别已关闭" : !config.CaptureRegionSet ? "请重新框选两个枪名，按文字高亮判断激活武器" : "等待 Apex 位于前台");
        }
        void SetStatus(string message, string eventKey = null)
        {
            Status = message;
            // Keep transitions, not every OCR frame. This bounded memory-only history explains pauses without log files.
            string key = eventKey ?? message;
            if (key != lastEvent)
            {
                lastEvent = key; history.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
                while (history.Count > 30) history.Dequeue();
            }
            if (Updated != null) Updated();
        }
        async void Tick(object sender, EventArgs args)
        {
            if (disposed || !config.AutoDetect || !config.CaptureRegionSet || failed) return;
            IntPtr window; Rectangle bounds;
            if (!ApexWindow.Foreground(out window, out bounds))
            {
                if (lastWindow != IntPtr.Zero) { generation++; lastWindow = IntPtr.Zero; tracker.Reset(); }
                const string background = "已暂停：Apex 不在前台；切回游戏自动继续";
                if (Status != background) SetStatus(background);
                return;
            }
            if (window != lastWindow || bounds != lastBounds) { generation++; tracker.Reset(); lastWindow = window; lastBounds = bounds; }
            if (busy) return;
            Rectangle region = ApexWindow.CaptureBounds(config, bounds);
            if (region.Width < 12 || region.Height < 6 || region.Width > 1600 || region.Height > 400)
            { SetStatus("识别区域不合适，请把两个枪名一起框入"); return; }
            int revision = generation;
            string language = config.OcrLanguage;
            busy = true;
            try
            {
                var watch = Stopwatch.StartNew();
                ActiveWeaponReading reading = await Task.Run(delegate
                {
                    // Check both sides of capture: an Alt-Tab mid-capture must never produce a classification.
                    if (Native.GetForegroundWindow() != window) return null;
                    using (var crop = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(crop)) g.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
                        if (Native.GetForegroundWindow() != window) return null;
                        if (ocr == null) ocr = new WindowsOcr();
                        return HudWeaponReader.Read(crop, ocr, language);
                    }
                });
                if (disposed || revision != generation || !config.AutoDetect) return;
                IntPtr current; Rectangle currentBounds;
                if (!ApexWindow.Foreground(out current, out currentBounds) || current != window || currentBounds != bounds) { Reset(); return; }
                LastMilliseconds = watch.Elapsed.TotalMilliseconds;
                Weapon found = reading == null ? null : reading.Active;
                Weapon confirmed = tracker.Observe(found);
                string active = confirmed == null ? "普通准星" : confirmed.Name + " · " + (confirmed.Kind == WeaponKind.Shotgun ? "霰弹枪" : confirmed.Kind == WeaponKind.Sniper ? "狙击枪" : "普通准星");
                string result = (reading == null ? "等待画面稳定" : reading.Message) + "；" + active;
                LastRecognizedText = reading == null ? "" : reading.RawText;
                LastResult = DateTime.Now.ToString("HH:mm:ss") + "  " + result;
                SetStatus(result + " · " + LastMilliseconds.ToString("0") + " ms", result);
            }
            catch (Exception error)
            {
                if (!disposed && revision == generation)
                {
                    failed = true; tracker.Reset();
                    SetStatus("识别已暂停：" + error.GetBaseException().Message);
                }
            }
            finally { busy = false; }
        }
        public void Dispose() { disposed = true; generation++; timer.Stop(); timer.Dispose(); }
    }

    public sealed class RegionPicker : Form
    {
        Point start, end;
        bool dragging;
        public Rectangle Selection { get; private set; }
        public RegionPicker(Rectangle bounds)
        {
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; Bounds = bounds; TopMost = true;
            BackColor = Color.Black; Opacity = 0.35; DoubleBuffered = true;
            ShowInTaskbar = false; Cursor = Cursors.Cross; KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button != MouseButtons.Left) return; start = end = e.Location; dragging = true; Capture = true; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            end = new Point(Math.Max(0, Math.Min(ClientSize.Width, e.X)), Math.Max(0, Math.Min(ClientSize.Height, e.Y))); Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false; Capture = false;
            Rectangle selected = Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
            if (selected.Width < 24 || selected.Height < 10 || selected.Width > 1000 || selected.Height > 200) { Invalidate(); return; }
            Selection = selected; DialogResult = DialogResult.OK; Close();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var font = new Font("Microsoft YaHei UI", 18, FontStyle.Bold))
                e.Graphics.DrawString("把两把武器的名称一起框入，包含亮的和暗的；避开弹药与拾取提示。Esc 取消。", font, Brushes.White, 24, 24);
            using (var pen = new Pen(Color.Lime, 3)) e.Graphics.DrawRectangle(pen, Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));
        }
    }
}

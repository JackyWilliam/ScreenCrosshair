using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using ScreenCrosshair;

public static class RecognitionTests
{
    static int checks, exitCode;
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); checks++; }
    static Weapon Find(string text) { return WeaponCatalog.Match(text); }
    static void UnitChecks()
    {
        Assert(Find("EVA-8 AUTO").Kind == WeaponKind.Shotgun && Find("MASTIFF").Kind == WeaponKind.Shotgun && Find("莫桑比克").Kind == WeaponKind.Shotgun, "Shotgun weapon categories");
        Assert(Find("LONGBOW DMR").Kind == WeaponKind.Sniper && Find("克雷贝尔 50口径狙击枪").Kind == WeaponKind.Sniper, "Sniper weapon categories");
        Assert(Find("和 平 捍 卫 者") != null && Find("和 平 捍 卫 者").Kind == WeaponKind.Shotgun, "Chinese OCR word spacing");
        Assert(Find("CHARGE RIFLE") != null && Find("CHARGE RIFLE").Kind == WeaponKind.Sniper, "English spacing");
        Assert(Find("SENTINEL PEACEKEEPER") == null, "Flat text cannot resolve two weapon names");
        Assert(Find("轻型弹药 120") == null && Find("SNIPER AMMO") == null && Find("") == null, "Unknown text and ammo do not select a gun");
        Assert(Find("TRIPLE TAKE").Kind == WeaponKind.Ordinary, "Marksman category remains ordinary");
        Assert(Find("R-301 CARBINE").Name == "R-301" && Find("CARBINE") == null, "CAR alias cannot match the word CARBINE");
        var tracker = new StableWeaponTracker();
        Assert(tracker.Observe(Find("SENTINEL")) == null, "One frame cannot switch style");
        Assert(tracker.Observe(Find("SENTINEL")).Kind == WeaponKind.Sniper, "Two identical reads confirm sniper");
        Assert(tracker.Observe(Find("PEACEKEEPER")).Kind == WeaponKind.Sniper, "Single transient read preserves current style");
        Assert(tracker.Observe(null).Kind == WeaponKind.Sniper, "First unknown preserves current style");
        tracker.Observe(null); Assert(tracker.Observe(null) == null, "Three unknown reads restore ordinary style");
        tracker.Observe(Find("MASTIFF")); tracker.Reset(); Assert(tracker.Observe(Find("MASTIFF")) == null, "Reset clears confirmation history");
        Settings old;
        using (var text = new StringReader("<Settings><ColorIndex>3</ColorIndex><Shape>2</Shape><Length>13</Length></Settings>")) old = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(text);
        old.Validate(); Assert(old.Length == 13 && old.Shape == 2 && old.ForegroundColor == Settings.Colors[3] && !old.AutoDetect, "Legacy configuration migration preserves appearance");
        Color parsed;
        Assert(Settings.TryParseColor("#010203", out parsed), "Custom RGB supports old transparency key");
        Assert(!Settings.TryParseColor("#12345Z", out parsed), "Invalid RGB is rejected");
        var config = new Settings { ColorHex = "#123456", Shape = 4, CenterDot = false, Outline = false };
        using (var image = new Bitmap(128, 128))
        using (Graphics g = Graphics.FromImage(image))
        {
            g.Clear(Color.Magenta); Renderer.Draw(g, config, 64, 64);
            Assert(image.GetPixel(64, 56).ToArgb() == config.ForegroundColor.ToArgb(), "Sniper top stroke uses custom RGB");
            Assert(image.GetPixel(64, 72).ToArgb() == Color.Magenta.ToArgb(), "Sniper has no bottom stroke");
            g.Clear(Color.Magenta); Renderer.Draw(g, config, 64, 64, 3);
            Assert(image.GetPixel(64, 64).ToArgb() == config.ForegroundColor.ToArgb(), "Shotgun always contains center dot");
            Assert(image.GetPixel(72, 64).ToArgb() == config.ForegroundColor.ToArgb(), "Shotgun contains ring");
        }
        config.CaptureLayoutVersion = 2; config.CaptureRegionSet = true; config.CaptureX = double.NaN; config.Validate();
        Assert(!config.CaptureRegionSet, "Invalid capture bounds are disabled");
        config.CaptureX = .8; config.CaptureY = .9; config.CaptureWidth = .15; config.CaptureHeight = .04; config.CaptureRegionSet = true;
        config.AutoDetect = true; config.OcrLanguage = "en-US"; config.ScanInterval = 2000;
        config.CaptureLayoutVersion = 0; config.Validate();
        Assert(!config.CaptureRegionSet && config.AutoDetect && config.ColorHex == "#123456", "Old single-slot crop requires reselect without losing appearance or mode");
        config.CaptureRegionSet = true; config.CaptureLayoutVersion = 2;
        var serializer = new XmlSerializer(typeof(Settings));
        using (var output = new StringWriter())
        {
            serializer.Serialize(output, config);
            using (var input = new StringReader(output.ToString()))
            {
                var saved = (Settings)serializer.Deserialize(input); saved.Validate();
                Assert(saved.AutoDetect && saved.CaptureRegionSet && saved.CaptureX == .8 && saved.ScanInterval == 2000 && saved.OcrLanguage == "en-US" && saved.ColorHex == "#123456", "Recognition region, language, interval and custom color round-trip");
            }
        }
    }

    static void ImagePipeline()
    {
        var reader = new WindowsOcr(); var tracker = new StableWeaponTracker();
        using (var image = new Bitmap(480, 72))
        using (Graphics g = Graphics.FromImage(image))
        using (var font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold))
        {
            string[] texts = { "PEACEKEEPER", "SENTINEL", "R-301", "和平捍卫者", "哨兵" };
            WeaponKind[] kinds = { WeaponKind.Shotgun, WeaponKind.Sniper, WeaponKind.Ordinary, WeaponKind.Shotgun, WeaponKind.Sniper };
            for (int i = 0; i < texts.Length; i++)
            {
                g.Clear(Color.FromArgb(24, 29, 38)); g.DrawString(texts[i], font, Brushes.White, 8, 8);
                string language = i < 3 ? "en-US" : "zh-Hans-CN";
                tracker.Reset(); OcrReading raw = reader.ReadLayout(image, language); Console.WriteLine("OCR " + texts[i] + " -> " + raw.Text); Weapon first = ActiveWeaponDetector.Read(image, raw).Active;
                Assert(tracker.Observe(first) == null, "OCR first frame held: " + texts[i]);
                Weapon second = tracker.Observe(ActiveWeaponDetector.Read(image, reader.ReadLayout(image, language)).Active);
                Assert(second != null && second.Kind == kinds[i], "Image -> Windows OCR -> class -> stable style: " + texts[i]);
            }
            g.Clear(Color.Black);
            tracker.Observe(ActiveWeaponDetector.Read(image, reader.ReadLayout(image, "zh-Hans-CN")).Active); tracker.Observe(null);
            Assert(tracker.Observe(null) == null, "Blank image clears confirmed weapon after three samples");
        }
    }

    static ActiveWeaponReading DualHud(WindowsOcr reader, string left, string right, Color leftColor, Color rightColor, string language)
    {
        using (var image = new Bitmap(960, 86))
        using (Graphics g = Graphics.FromImage(image))
        using (var font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold))
        using (var leftBrush = new SolidBrush(leftColor))
        using (var rightBrush = new SolidBrush(rightColor))
        {
            g.Clear(Color.FromArgb(28, 43, 39));
            using (var activeBackground = new SolidBrush(Color.FromArgb(125, 35, 8))) g.FillRectangle(activeBackground, 480, 0, 480, 86);
            // The permanent white hotkey tiles must not make the inactive label look active.
            g.FillRectangle(Brushes.White, 10, 18, 32, 42); g.FillRectangle(Brushes.White, 490, 18, 32, 42);
            g.DrawString("1", font, Brushes.Black, 9, 17); g.DrawString("2", font, Brushes.Black, 489, 17);
            g.DrawString(left, font, leftBrush, 50, 18); g.DrawString(right, font, rightBrush, 530, 18);
            return ActiveWeaponDetector.Read(image, reader.ReadLayout(image, language));
        }
    }

    static void HighlightChecks()
    {
        var reader = new WindowsOcr(); Color dim = Color.FromArgb(158, 164, 160);
        ActiveWeaponReading right = DualHud(reader, "HEMLOK", "MOZAMBIQUE", dim, Color.White, "en-US");
        Assert(right.Labels.Count == 2 && right.Active == Find("MOZAMBIQUE"), "Both persistent names: bright right Mozambique wins");
        ActiveWeaponReading left = DualHud(reader, "HEMLOK", "MOZAMBIQUE", Color.White, dim, "en-US");
        Assert(left.Labels.Count == 2 && left.Active == Find("HEMLOK"), "Brightness swap selects left ordinary weapon");
        Assert(right.Labels.All(label => label.Bounds.Left >= 45 && label.Bounds.Left != 490), "White slot tiles excluded from name bounds");
        Assert(DualHud(reader, "SENTINEL", "PEACEKEEPER", Color.White, dim, "en-US").Active == Find("SENTINEL"), "Bright left sniper wins over inactive shotgun");
        Assert(DualHud(reader, "SENTINEL", "PEACEKEEPER", dim, Color.White, "en-US").Active == Find("PEACEKEEPER"), "Same pair switches to right shotgun");
        Assert(DualHud(reader, "SENTINEL", "PEACEKEEPER", Color.White, Color.White, "en-US").Active == null, "Equally bright names do not pick arbitrary slot");
        Assert(DualHud(reader, "SENTINEL", "PEACEKEEPER", dim, dim, "en-US").Active == null, "Two dim names do not activate");
        Assert(DualHud(reader, "SENTINEL", "PEACEKEEPER", Color.White, Color.FromArgb(240, 240, 240), "en-US").Active == null, "Small brightness difference is uncertain");
        Assert(DualHud(reader, "SENTINEL", "", dim, dim, "en-US").Active == null, "Only readable inactive name cannot activate");
        Assert(DualHud(reader, "SENTINEL", "", Color.White, dim, "en-US").Active == Find("SENTINEL"), "One clearly bright readable name can activate");
        Assert(DualHud(reader, "莫桑比克", "赫姆洛克", Color.White, dim, "zh-Hans-CN").Active == Find("莫桑比克"), "Chinese split-word boxes select bright shotgun");
        Assert(DualHud(reader, "哨兵", "莫桑比克", dim, Color.White, "zh-Hans-CN").Active == Find("莫桑比克"), "Chinese dim sniper is ignored");
        var tracker = new StableWeaponTracker(); tracker.Observe(right.Active); tracker.Observe(right.Active);
        Assert(tracker.Observe(left.Active) == right.Active && tracker.Observe(left.Active) == left.Active, "Highlight switching still requires two consistent frames");
    }

    static void UiChecks()
    {
        var config = new Settings { ColorHex = "#010203" };
        using (var overlay = new Overlay(config, false))
        using (var settings = new SettingsWindow(overlay))
        {
            overlay.Show(); settings.Show(); Application.DoEvents();
            Assert(overlay.TransparencyKey.ToArgb() != config.ForegroundColor.ToArgb(), "Custom transparency-key color remains visible");
            SliderRow[] sliders = settings.Controls.OfType<SliderRow>().ToArray();
            Assert(sliders.Length == 3, "Three appearance sliders available");
            sliders[0].Value = 19; sliders[1].Value = 7; sliders[2].Value = 4;
            Assert(config.Length == 19 && config.Gap == 7 && config.Thickness == 4, "Sliders update live settings");
            TextBox hex = settings.Controls.OfType<TextBox>().Single(); hex.Text = "#AA33CC";
            Assert(config.ForegroundColor.ToArgb() == Color.FromArgb(170, 51, 204).ToArgb(), "HEX input updates live color");
            hex.Text = "#AA33CZ"; Assert(config.ColorHex == "#AA33CC", "Invalid HEX preserves last valid color");
            config.AutoDetect = false; overlay.Changed(); Assert(overlay.Recognition.Status == "自动识别已关闭", "Disabled recognition reports stopped");
            settings.Close(); overlay.Close();
        }
    }

    static async Task WaitFor(Func<bool> condition, string description, int timeout = 7000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < timeout) await Task.Delay(50);
        Assert(condition(), description);
    }

    static async Task Integration(Form game)
    {
        string gun = "PEACEKEEPER"; bool rightActive = false;
        var font = new Font("Segoe UI", 23F, FontStyle.Bold);
        game.Paint += delegate(object sender, PaintEventArgs e)
        {
            e.Graphics.DrawString("Synthetic HUD test - not Apex", game.Font, Brushes.Silver, 20, 20);
            e.Graphics.DrawString(gun, font, rightActive ? Brushes.Gray : Brushes.White, 22, 99);
            e.Graphics.DrawString("SENTINEL", font, rightActive ? Brushes.White : Brushes.Gray, 370, 99);
        };
        game.Invalidate(); game.Activate(); SetForegroundWindow(game.Handle);
        var config = new Settings { AutoDetect = true, OcrLanguage = "en-US", ScanInterval = 500, CaptureLayoutVersion = 2, CaptureRegionSet = true, CaptureX = 20.0 / 640, CaptureY = 94.0 / 320, CaptureWidth = 600.0 / 640, CaptureHeight = 54.0 / 320 };
        using (var overlay = new Overlay(config, false))
        {
            overlay.Recognition.Updated += delegate { Console.WriteLine("STATUS " + overlay.Recognition.Status); };
            overlay.Show(); game.Activate(); SetForegroundWindow(game.Handle);
            await WaitFor(delegate { return overlay.Recognition.DisplayShape == 3; }, "Real cropped GDI capture + Windows OCR selects shotgun");
            Console.WriteLine("Shotgun capture+OCR ms=" + overlay.Recognition.LastMilliseconds.ToString("0.0"));
            rightActive = true; game.Invalidate();
            await WaitFor(delegate { return overlay.Recognition.DisplayShape == 4; }, "Live brightness swap selects sniper with both names unchanged");
            rightActive = false; gun = "R-301"; game.Invalidate();
            await WaitFor(delegate { return overlay.Recognition.DisplayShape == 0 && overlay.Recognition.Status.Contains("R-301"); }, "Ordinary weapon restores configured style");
            gun = "MASTIFF"; game.Invalidate();
            await WaitFor(delegate { return overlay.Recognition.DisplayShape == 3; }, "Recognizes another shotgun");
            gun = ""; game.Invalidate();
            await WaitFor(delegate { return overlay.Recognition.DisplayShape == 0 && overlay.Recognition.Status.StartsWith("枪名高亮不明确"); }, "Only dim reserve name remains: restore ordinary style");
            config.OcrLanguage = "zh-Hans-CN"; overlay.Changed(); gun = "哨兵"; game.Invalidate();
            await WaitFor(delegate { return overlay.Recognition.DisplayShape == 4; }, "Chinese HUD capture selects sniper");
            config.AutoDetect = false; overlay.Changed();
            await Task.Delay(650); Assert(overlay.Recognition.Status == "自动识别已关闭", "Disable stops recognition");
            config.ColorHex = "#010203"; overlay.Changed();
            Assert(overlay.TransparencyKey.ToArgb() != config.ForegroundColor.ToArgb(), "Custom transparency-key color remains visible");
            using (var settings = new SettingsWindow(overlay))
            {
                settings.Show(); await Task.Delay(150);
                SliderRow[] sliders = settings.Controls.OfType<SliderRow>().ToArray();
                Assert(sliders.Length == 3, "All three numeric appearance controls replaced by sliders");
                sliders[0].Value = 19; sliders[1].Value = 7; sliders[2].Value = 4;
                Assert(config.Length == 19 && config.Gap == 7 && config.Thickness == 4, "Sliders update live settings");
                TextBox hex = settings.Controls.OfType<TextBox>().Single(); hex.Text = "#AA33CC";
                Assert(config.ForegroundColor.ToArgb() == Color.FromArgb(170, 51, 204).ToArgb(), "HEX input updates live color");
                hex.Text = "#AA33CZ"; Assert(config.ColorHex == "#AA33CC", "Partial invalid HEX preserves last valid color");
                settings.Close();
            }
            overlay.Close();
        }
        font.Dispose();
    }

    [STAThread]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try { UnitChecks(); ImagePipeline(); HighlightChecks(); UiChecks(); } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        if (!args.Contains("--interactive")) { Console.WriteLine("SUCCESS: " + checks + " assertions; interactive screen-capture test not requested."); return 0; }
        // The executable is named r5apex solely so the production foreground guard can be tested end to end.
        // All captured pixels belong to this synthetic window; no game process is modified or inspected.
        using (var game = new Form { Text = "ScreenCrosshair synthetic test", TopMost = true, ClientSize = new Size(640, 320), BackColor = Color.FromArgb(24, 29, 38), StartPosition = FormStartPosition.CenterScreen })
        {
            game.Shown += async delegate
            {
                try { await Integration(game); Console.WriteLine("SUCCESS: " + checks + " assertions."); }
                catch (Exception e) { Console.Error.WriteLine(e); exitCode = 1; }
                finally { game.Close(); }
            };
            Application.Run(game);
        }
        return exitCode;
    }
}

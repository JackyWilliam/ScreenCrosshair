using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using ScreenCrosshair;

public static class RecognitionTests
{
    static int checks, exitCode;
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
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
        Assert(new[] { "G7", "G7 SCOUT", "G7侦察枪", "30-30 REPEATER", "3030", "BOCEK", "波塞克", "三重击", "TRIPLE TAKE" }.All(name => Find(name) != null && Find(name).Kind == WeaponKind.Sniper), "Marksman aliases all use the sniper profile");
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
            return HudWeaponReader.Read(image, reader, language);
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

    static void NumericHudChecks()
    {
        var reader = new WindowsOcr(); Color dim = Color.FromArgb(158, 164, 160);
        Weapon repeater = Find("30-30"), shotgun = Find("莫桑比克");
        using (var image = new Bitmap(467, 171))
        using (Graphics g = Graphics.FromImage(image))
        using (var font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold))
        using (var ammoFont = new Font("Segoe UI", 25F, FontStyle.Bold))
        {
            for (int state = 0; state < 4; state++)
            {
                // A tall HUD selection includes bright ammo and slot keys above much smaller gun names.
                g.Clear(Color.FromArgb(55, 27, 21));
                g.DrawString("02", ammoFont, Brushes.White, 310, 20);
                g.FillRectangle(Brushes.White, 24, 128, 26, 26); g.FillRectangle(Brushes.White, 239, 128, 26, 26);
                using (var leftBrush = new SolidBrush(state == 0 || state == 3 ? Color.White : dim))
                using (var rightBrush = new SolidBrush(state == 1 || state == 3 ? Color.White : dim))
                {
                    g.DrawString("30-30", font, leftBrush, 103, 123);
                    g.DrawString("莫桑比克", font, rightBrush, 293, 123);
                }
                ActiveWeaponReading result = HudWeaponReader.Read(image, reader, "zh-Hans-CN");
                Console.WriteLine("Numeric HUD " + state + " -> " + result.RawText + " / " + result.Message);
                Assert(state == 0 ? result.Active == repeater : state == 1 ? result.Active == shotgun : result.Active == null,
                    "Tall numeric HUD: active left/right, both dim, and equal brightness state " + state);
            }
            g.Clear(Color.Black);
            Assert(HudWeaponReader.Read(image, reader, "zh-Hans-CN").Active == null, "Contrast retry cannot activate a blank HUD");
        }
        Assert(Find("50-30") == null && Find("3 囗 囗 0 0") == null && Find("30") == null,
            "Numeric recovery does not turn uncertain OCR fragments into a weapon alias");
        Assert(new[] { "3 囗 一 3 囗", "3口-3口", "3O–30", "30一3〇", "3\t囗\t一\t3\t囗" }.All(s => Find(s) == repeater),
            "Complete 30-30 HUD token tolerates Chinese zero and dash glyphs");
        Assert(new[] { "3囗", "3囗3囗", "5囗一3囗", "3囗一8囗", "3囗一3", "3囗一3囗0", "13囗一3囗", "弹药3囗一3囗", "3囗一3囗R301" }.All(s => Find(s) != repeater),
            "Incomplete, prefixed, suffixed and different-digit OCR fragments are not corrected to 30-30");
        using (var image = new Bitmap(200, 50))
        using (Graphics g = Graphics.FromImage(image))
        {
            var text = new OcrReading { Text = "克雷贝尔 3 囗 一 3 囗" };
            var line = new OcrLineBox(); text.Lines.Add(line);
            line.Words.Add(new OcrWordBox { Text = "克雷贝尔", Bounds = new RectangleF(10, 10, 40, 25) });
            for (int i = 0; i < 5; i++) line.Words.Add(new OcrWordBox { Text = "3囗一3囗"[i].ToString(), Bounds = new RectangleF(100 + i * 15, 10, 12, 25) });
            for (int state = 0; state < 4; state++)
            {
                g.Clear(Color.FromArgb(20, 20, 20));
                for (int i = 0; i < line.Words.Count; i++)
                {
                    bool bright = state == 3 || (i == 0 ? state == 1 : state == 0);
                    // Simulated OCR boxes retain dark margins and bright/dim strokes independently of the misread glyph.
                    RectangleF box = line.Words[i].Bounds; box.Inflate(-3, -3);
                    using (var brush = new SolidBrush(bright ? Color.White : Color.FromArgb(170, 170, 170))) g.FillRectangle(brush, box);
                }
                var result = ActiveWeaponDetector.Read(image, text);
                Assert(result.Labels.Count == 2 && (state == 0 ? result.Active == repeater : state == 1 ? result.Active == Find("克雷贝尔") : result.Active == null),
                    "Corrected OCR token still follows actual glyph brightness: " + state);
            }
            line.Words.RemoveRange(1, 5);
            g.Clear(Color.FromArgb(20, 20, 20));
            using (var brush = new SolidBrush(Color.FromArgb(170, 170, 170))) g.FillRectangle(brush, 13, 13, 34, 19);
            var dimOnly = ActiveWeaponDetector.Read(image, text);
            Assert(dimOnly.Active == null && dimOnly.Message.StartsWith("只读到暗色枪名：克雷贝尔"), "Dim-only OCR explains a possibly missing active label");
        }
    }

    static void ProfileChecks()
    {
        var serializer = new XmlSerializer(typeof(Settings));
        Settings legacy;
        using (var input = new StringReader("<Settings><Length>14</Length><Gap>6</Gap><Thickness>3</Thickness><ColorIndex>2</ColorIndex><ColorHex>#AABBCC</ColorHex><Outline>false</Outline><CenterDot>true</CenterDot><AutoDetect>true</AutoDetect><ScanInterval>500</ScanInterval><CaptureLayoutVersion>2</CaptureLayoutVersion><CaptureRegionSet>true</CaptureRegionSet><CaptureX>0.7</CaptureX><CaptureY>0.8</CaptureY><CaptureWidth>0.2</CaptureWidth><CaptureHeight>0.1</CaptureHeight></Settings>"))
            legacy = (Settings)serializer.Deserialize(input);
        legacy.Validate();
        Assert(legacy.Length == 14 && legacy.Shotgun.Length == 14 && legacy.Sniper.Length == 14 && legacy.Shotgun.ColorHex == "#AABBCC" && legacy.Sniper.ColorHex == "#AABBCC", "Legacy shared colors and dimensions copied to all profiles");
        Assert(legacy.Shape == 0 && legacy.Shotgun.Shape == 3 && legacy.Sniper.Shape == 4 && !legacy.Shotgun.Outline && legacy.Sniper.CenterDot, "Migration preserves appearance and class-specific shapes");
        Assert(legacy.CaptureRegionSet && legacy.CaptureLayoutVersion == 2 && legacy.AutoDetect && legacy.ScanInterval == 500, "Profile migration preserves calibrated region and recognition settings");
        legacy.Shotgun.Length = 26; legacy.Shotgun.ColorHex = "#FF0000"; legacy.Sniper.Thickness = 5; legacy.Sniper.Gap = 12;
        legacy.Validate();
        Assert(legacy.Length == 14 && legacy.Sniper.Length == 14 && legacy.Thickness == 3 && legacy.Shotgun.Thickness == 3, "Profile edits do not share mutable values");
        using (var output = new StringWriter())
        {
            serializer.Serialize(output, legacy);
            using (var input = new StringReader(output.ToString()))
            {
                var saved = (Settings)serializer.Deserialize(input); saved.Validate();
                Assert(saved.Length == 14 && saved.Shotgun.Length == 26 && saved.Shotgun.ColorHex == "#FF0000" && saved.Sniper.Thickness == 5 && saved.Sniper.Gap == 12, "All three profiles survive XML reload independently");
            }
        }
        legacy.ResetProfile(WeaponKind.Shotgun);
        Assert(legacy.Shotgun.Shape == 3 && legacy.Shotgun.Length == 8 && legacy.Shotgun.ColorHex == "" && legacy.Length == 14 && legacy.Sniper.Thickness == 5, "Reset affects only chosen profile");
        legacy.Shotgun = null; legacy.Validate();
        Assert(legacy.Shotgun.Length == 14 && legacy.Sniper.Thickness == 5, "Missing one profile repairs only that profile");

        legacy.AutoDetect = false; legacy.ColorHex = "#010203"; legacy.Shotgun.ColorHex = "#020304"; legacy.Sniper.ColorHex = "#030405";
        using (var overlay = new Overlay(legacy, false))
        {
            var tracker = (StableWeaponTracker)typeof(RecognitionController).GetField("tracker", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(overlay.Recognition);
            Assert(new[] { legacy.ForegroundColor, legacy.Shotgun.ForegroundColor, legacy.Sniper.ForegroundColor }.All(c => c.ToArgb() != overlay.TransparencyKey.ToArgb()), "Per-pixel alpha leaves all RGB colors available");
            tracker.Observe(Find("MASTIFF")); tracker.Observe(Find("MASTIFF"));
            Assert(ReferenceEquals(overlay.Recognition.DisplayStyle, legacy.Shotgun), "Confirmed shotgun selects complete shotgun profile");
            legacy.Shotgun.Shape = 2; overlay.Changed(false);
            Assert(overlay.Recognition.DisplayShape == 2 && ReferenceEquals(overlay.Recognition.DisplayStyle, legacy.Shotgun), "Appearance edits preserve recognition and use chosen profile shape");
            tracker.Observe(Find("SENTINEL")); tracker.Observe(Find("SENTINEL"));
            Assert(ReferenceEquals(overlay.Recognition.DisplayStyle, legacy.Sniper), "Confirmed sniper selects complete sniper profile");
            foreach (string name in new[] { "G7 SCOUT", "30-30", "BOCEK", "TRIPLE TAKE" })
            {
                tracker.Reset(); tracker.Observe(Find(name)); tracker.Observe(Find(name));
                Assert(ReferenceEquals(overlay.Recognition.DisplayStyle, legacy.Sniper), "Confirmed marksman selects complete sniper profile: " + name);
            }
            tracker.Observe(null); tracker.Observe(null); tracker.Observe(null);
            Assert(ReferenceEquals(overlay.Recognition.DisplayStyle, legacy), "Unknown readings restore complete ordinary profile");
            tracker.Observe(Find("MASTIFF")); tracker.Observe(Find("MASTIFF")); overlay.Changed();
            Assert(ReferenceEquals(overlay.Recognition.DisplayStyle, legacy), "Recognition configuration reset restores ordinary profile");
            int before = overlay.Recognition.HistoryText.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length;
            overlay.Recognition.Reset();
            Assert(overlay.Recognition.HistoryText.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length == before, "Repeated same state does not flood history");
            for (int i = 0; i < 50; i++) { legacy.AutoDetect = !legacy.AutoDetect; overlay.Recognition.Reset(); }
            Assert(overlay.Recognition.HistoryText.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length == 30, "Status history is bounded to 30 transitions");
        }
    }

    static void CaptureRestrictionChecks()
    {
        var config = new Settings { AutoDetect = true, CaptureRegionSet = true, CaptureLayoutVersion = 2 };
        config.Validate();
        // Only this unshown, test-owned window is modified; no game handle or foreground capture is used.
        using (var window = new Form { Opacity = 0.99, ShowInTaskbar = false })
        using (var recognition = new RecognitionController(config))
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var controller = typeof(RecognitionController);
            var guard = controller.GetMethod("PauseIfCaptureRestricted", flags);
            var tracker = (StableWeaponTracker)controller.GetField("tracker", flags).GetValue(recognition);
            var timer = (System.Windows.Forms.Timer)controller.GetField("timer", flags).GetValue(recognition);
            timer.Stop();
            IntPtr handle = window.Handle;
            Assert(SetWindowDisplayAffinity(handle, 0) && !(bool)guard.Invoke(recognition, new object[] { handle }), "Unrestricted window allows recognition");
            foreach (uint affinity in new uint[] { 1, 0x11 })
            {
                tracker.Observe(Find("MASTIFF")); tracker.Observe(Find("MASTIFF"));
                controller.GetProperty("LastRecognizedText").GetSetMethod(true).Invoke(recognition, new object[] { "stale desktop text" });
                controller.GetProperty("LastMilliseconds").GetSetMethod(true).Invoke(recognition, new object[] { 42.0 });
                Assert(SetWindowDisplayAffinity(handle, affinity) && ApexWindow.CaptureRestricted(handle), "Native capture restriction detected: " + affinity);
                Assert((bool)guard.Invoke(recognition, new object[] { handle }) && recognition.Status.Contains("窗口限制截图"), "Capture restriction pauses with an explicit explanation");
                Assert(ReferenceEquals(recognition.DisplayStyle, config) && recognition.LastRecognizedText == "" && recognition.LastMilliseconds == 0
                    && recognition.LastResult.Contains("窗口限制截图"), "Capture pause clears stale OCR and restores ordinary profile");
                string history = recognition.HistoryText;
                int generation = (int)controller.GetField("generation", flags).GetValue(recognition);
                guard.Invoke(recognition, new object[] { handle });
                Assert(recognition.HistoryText == history && (int)controller.GetField("generation", flags).GetValue(recognition) > generation,
                    "Repeated restricted polls invalidate pending work without flooding history");
                Assert(SetWindowDisplayAffinity(handle, 0) && !(bool)guard.Invoke(recognition, new object[] { handle })
                    && !(bool)controller.GetField("failed", flags).GetValue(recognition), "Lifting restriction allows retry without manual reset");
                Assert(tracker.Observe(Find("MASTIFF")) == null && tracker.Observe(Find("MASTIFF")) == Find("MASTIFF"),
                    "After capture restriction two fresh observations are required");
            }
            Assert(!ApexWindow.CaptureRestricted(new IntPtr(-1)), "Failed affinity query is not reported as capture restriction");
            Assert(controller.GetField("ocr", flags).GetValue(recognition) == null, "Capture restriction checks do not initialize OCR");
        }
    }

    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
    }

    sealed class TestSettingsWindow : SettingsWindow
    {
        public TestSettingsWindow(Overlay owner) : base(owner) { ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000); }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnShown(EventArgs e) { }
    }

    static void UiChecks()
    {
        var config = new Settings { ColorHex = "#010203" };
        using (var overlay = new Overlay(config, false))
        using (var settings = new TestSettingsWindow(overlay))
        {
            overlay.Show(); settings.Show(); Application.DoEvents();
            Assert(overlay.TransparencyKey.ToArgb() != config.ForegroundColor.ToArgb(), "Custom RGB remains independent of transparency");
            SliderRow[] sliders = Descendants(settings).OfType<SliderRow>().ToArray();
            Assert(sliders.Length == 3, "Three appearance sliders available");
            TextBox pixels = sliders[2].Controls.OfType<TextBox>().Single();
            pixels.Text = "1.25";
            Assert(config.Thickness == 1.25 && sliders[2].Value == 1.25, "Typed fractional pixels update actual style and slider");
            pixels.Text = ""; pixels.Text = "abc"; pixels.Text = "999";
            Assert(config.Thickness == 1.25, "Incomplete, nonnumeric and out-of-range input preserve last valid size");
            sliders[2].Value = 2.75;
            Assert(pixels.Text == "2.75" && config.Thickness == 2.75, "Slider synchronizes numeric input without integer rounding");
            sliders[0].Value = 19; sliders[1].Value = 7; sliders[2].Value = 4;
            Assert(config.Length == 19 && config.Gap == 7 && config.Thickness == 4, "Sliders update live settings");
            TextBox hex = Descendants(settings).OfType<TextBox>().Single(c => c.Name == "ColorHex"); hex.Text = "#AA33CC";
            Assert(config.ForegroundColor.ToArgb() == Color.FromArgb(170, 51, 204).ToArgb(), "HEX input updates live color");
            hex.Text = "#AA33CZ"; Assert(config.ColorHex == "#AA33CC", "Invalid HEX preserves last valid color");
            ComboBox profile = Descendants(settings).OfType<ComboBox>().Single(c => c.Name == "EditingProfile");
            profile.SelectedIndex = 1; sliders[0].Value = 24; sliders[2].Value = 6; hex.Text = "#FF8800";
            Assert(config.Shotgun.Length == 24 && config.Shotgun.Thickness == 6 && config.Shotgun.ColorHex == "#FF8800" && config.Length == 19 && config.ColorHex == "#AA33CC", "UI edits shotgun profile without changing ordinary");
            Assert(ReferenceEquals(Descendants(settings).OfType<Preview>().Single().Config, config.Shotgun), "Preview follows selected profile");
            Assert(ReferenceEquals(overlay.Recognition.DisplayStyle, config), "Selecting editor profile alone does not switch active overlay");
            profile.SelectedIndex = 2; sliders[1].Value = 11; hex.Text = "#00BBFF";
            Assert(config.Sniper.Gap == 11 && config.Sniper.ColorHex == "#00BBFF" && config.Shotgun.ColorHex == "#FF8800", "UI sniper profile is independent of shotgun");
            profile.SelectedIndex = 0;
            Assert(sliders[0].Value == 19 && sliders[1].Value == 7 && sliders[2].Value == 4 && hex.Text == "#AA33CC", "Switching editor back restores ordinary control values");
            profile.SelectedIndex = 1;
            Descendants(settings).OfType<Button>().Single(b => b.Text == "重置当前").PerformClick();
            Assert(config.Shotgun.Length == 8 && config.Shotgun.Shape == 3 && config.Length == 19 && config.Sniper.Gap == 11, "UI reset is limited to selected profile");
            CustomPreset savedPreset = config.SavePreset("我的细准星", new CrosshairStyle { Length = 12.75, Gap = 3.25, Thickness = 1.25, ColorHex = "#77DDCC" }, WeaponKind.Shotgun);
            ComboBox custom = Descendants(settings).OfType<ComboBox>().Single(c => c.Name == "CustomPresets");
            custom.Items.Add(savedPreset); custom.SelectedItem = savedPreset; settings.ApplySelectedPreset();
            Assert(config.Shotgun.Thickness == 1.25 && config.Shotgun.Length == 12.75 && config.Length == 19, "Applying named preset affects only editor profile");
            sliders[2].Value = 2.5;
            Assert(savedPreset.Style.Thickness == 1.25, "Live edits after applying leave saved preset unchanged");
            ComboBox shape = Descendants(settings).OfType<ComboBox>().Single(c => c.Name == "CrosshairShape");
            shape.SelectedIndex = 6;
            int mercedes = CrosshairParts.Top | CrosshairParts.BottomLeft | CrosshairParts.BottomRight;
            Assert(config.Shotgun.PartsForShape(config.Shotgun.Shape) == mercedes, "Mercedes template selects three spokes");
            shape.SelectedIndex = 5;
            Assert(config.Shotgun.CustomParts == mercedes && config.Shotgun.DiagonalAngle == 30, "Custom mode preserves the selected Mercedes template and angle");
            CheckBox top = Descendants(settings).OfType<CheckBox>().Single(c => c.Name == "Part1");
            CheckBox ring = Descendants(settings).OfType<CheckBox>().Single(c => c.Name == "Part16");
            top.Checked = false; ring.Checked = true;
            Assert(config.Shotgun.CustomParts == (CrosshairParts.BottomLeft | CrosshairParts.BottomRight | CrosshairParts.Ring) && config.Shape == 0, "Independent UI switches remove one spoke and add ring only to selected profile");
            CustomPreset customParts = config.SavePreset("自定义部件", config.Shotgun, WeaponKind.Shotgun);
            top.Checked = true;
            Assert((customParts.Style.CustomParts & CrosshairParts.Top) == 0, "Saved custom parts remain isolated from live switches");
            using (var dialog = new PresetDialog(config, savedPreset, WeaponKind.Shotgun))
            {
                Assert(Descendants(dialog).OfType<Button>().Any(b => b.Text == "更新"), "Existing preset clearly offers update");
                Descendants(dialog).OfType<TextBox>().Single().Text = "";
                Assert(Descendants(dialog).OfType<Button>().Single(b => b.Text == "保存").Enabled == false, "Empty preset name cannot be saved");
            }
            config.AutoDetect = false; overlay.Changed(); Assert(overlay.Recognition.Status == "自动识别已关闭", "Disabled recognition reports stopped");
            settings.Close(); overlay.Close();
        }
    }

    static Settings RoundTrip(Settings settings)
    {
        var serializer = new XmlSerializer(typeof(Settings));
        using (var text = new StringWriter())
        {
            serializer.Serialize(text, settings);
            using (var input = new StringReader(text.ToString())) { var saved = (Settings)serializer.Deserialize(input); saved.Validate(); return saved; }
        }
    }

    static void ScopedPresetChecks()
    {
        var config = new Settings();
        config.SavePreset("竞技", new CrosshairStyle { Length = 10.5 }, WeaponKind.Ordinary);
        Assert(config.Presets(WeaponKind.Shotgun).Count == 0 && config.Presets(WeaponKind.Sniper).Count == 0, "Fresh ordinary preset does not populate other categories");
        config.SavePreset("竞技", new CrosshairStyle { Shape = 3, Length = 20.5 }, WeaponKind.Shotgun);
        config.SavePreset("竞技", new CrosshairStyle { Shape = 4, Length = 30.5 }, WeaponKind.Sniper);
        Assert(config.Presets(WeaponKind.Ordinary)[0].Style.Length == 10.5 && config.Presets(WeaponKind.Shotgun)[0].Style.Length == 20.5 && config.Presets(WeaponKind.Sniper)[0].Style.Length == 30.5,
            "Same preset name stores independent styles in all three categories");
        config.SavePreset("竞技", new CrosshairStyle { Length = 24 }, WeaponKind.Shotgun);
        Assert(config.Presets(WeaponKind.Ordinary)[0].Style.Length == 10.5 && config.Presets(WeaponKind.Shotgun)[0].Style.Length == 24 && config.Presets(WeaponKind.Sniper)[0].Style.Length == 30.5,
            "Updating same-name shotgun preset leaves ordinary and sniper intact");
        config.Presets(WeaponKind.Shotgun).Clear();
        Settings restored = RoundTrip(config);
        Assert(restored.Presets(WeaponKind.Shotgun).Count == 0 && restored.Presets(WeaponKind.Ordinary).Count == 1 && restored.Presets(WeaponKind.Sniper).Count == 1,
            "Deleting last preset in one category stays isolated and empty after restart");

        const string legacyXml = "<Settings><Length>14</Length><AutoDetect>true</AutoDetect><CaptureLayoutVersion>2</CaptureLayoutVersion><CaptureRegionSet>true</CaptureRegionSet><CaptureX>0.7</CaptureX><CaptureY>0.8</CaptureY><CaptureWidth>0.2</CaptureWidth><CaptureHeight>0.1</CaptureHeight><CustomPresets><CustomPreset><Name>旧组合</Name><Style><Shape>5</Shape><Length>12.75</Length><CustomParts>113</CustomParts><DiagonalAngle>30</DiagonalAngle></Style></CustomPreset></CustomPresets></Settings>";
        Settings legacy;
        using (var text = new StringReader(legacyXml)) legacy = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(text);
        legacy.Validate();
        Assert(legacy.PresetLayoutVersion == 1 && legacy.Presets(WeaponKind.Ordinary).Count == 1 && legacy.Presets(WeaponKind.Shotgun).Count == 1 && legacy.Presets(WeaponKind.Sniper).Count == 1,
            "Legacy shared presets migrate once to three accessible lists");
        Assert(!ReferenceEquals(legacy.CustomPresets[0], legacy.ShotgunPresets[0]) && !ReferenceEquals(legacy.CustomPresets[0].Style, legacy.SniperPresets[0].Style),
            "Migrated preset records and styles are deep copies");
        legacy.ShotgunPresets[0].Style.CustomParts = 0;
        Assert(legacy.CustomPresets[0].Style.CustomParts == 113 && legacy.SniperPresets[0].Style.DiagonalAngle == 30 && legacy.Length == 14 && legacy.AutoDetect && legacy.CaptureRegionSet,
            "Migration isolates custom components and preserves active appearance and ROI");
        legacy.SniperPresets.Clear(); restored = RoundTrip(legacy);
        Assert(restored.SniperPresets.Count == 0 && restored.CustomPresets[0].Style.Length == 12.75 && restored.ShotgunPresets[0].Style.CustomParts == 0,
            "Legacy migration does not repopulate a deleted list on later restarts");
    }

    static void ScopedPresetUiChecks()
    {
        var config = new Settings();
        CustomPreset ordinary = config.SavePreset("日常", new CrosshairStyle { Length = 12 }, WeaponKind.Ordinary);
        CustomPreset shotgun = config.SavePreset("日常", new CrosshairStyle { Shape = 3, Length = 24 }, WeaponKind.Shotgun);
        CustomPreset sniper = config.SavePreset("狙击专用", new CrosshairStyle { Shape = 4, Gap = 10 }, WeaponKind.Sniper);
        using (var overlay = new Overlay(config, false))
        using (var window = new TestSettingsWindow(overlay))
        {
            window.Show(); Application.DoEvents();
            ComboBox profile = Descendants(window).OfType<ComboBox>().Single(c => c.Name == "EditingProfile");
            ComboBox presets = Descendants(window).OfType<ComboBox>().Single(c => c.Name == "CustomPresets");
            Assert(presets.Items.Count == 2 && ReferenceEquals(presets.Items[1], ordinary), "Ordinary UI lists only ordinary presets");
            presets.SelectedIndex = 1;
            profile.SelectedIndex = 1;
            Assert(presets.Items.Count == 2 && ReferenceEquals(presets.Items[1], shotgun) && presets.SelectedIndex == 0, "Switching to shotgun replaces list without leaking selection");
            presets.SelectedIndex = 1; window.ApplySelectedPreset();
            Assert(config.Shotgun.Length == 24 && config.Length == 8 && config.Sniper.Gap == 4, "Shotgun preset applies only to shotgun profile");
            profile.SelectedIndex = 2;
            Assert(presets.Items.Count == 2 && ReferenceEquals(presets.Items[1], sniper), "Sniper UI lists only sniper presets");
            presets.SelectedIndex = 1; window.ApplySelectedPreset();
            profile.SelectedIndex = 0;
            Assert(ReferenceEquals(presets.SelectedItem, ordinary) && config.Sniper.Gap == 10, "Each category remembers its own selection and applied values");
            using (var dialog = new PresetDialog(config, null, WeaponKind.Sniper))
            {
                TextBox name = Descendants(dialog).OfType<TextBox>().Single(); name.Text = "日常";
                Assert(Descendants(dialog).OfType<Button>().Any(b => b.Text == "保存" && b.Enabled), "Names in another category do not trigger overwrite prompt");
                name.Text = "狙击专用";
                Assert(Descendants(dialog).OfType<Button>().Any(b => b.Text == "更新"), "Overwrite prompt checks only the current category");
            }
            window.Close();
        }
    }

    static void CommonParameterChecks()
    {
        foreach (WeaponKind sourceKind in new[] { WeaponKind.Ordinary, WeaponKind.Shotgun, WeaponKind.Sniper })
        {
            var config = new Settings { AutoDetect = true, OcrLanguage = "en-US", ScanInterval = 2000,
                CaptureRegionSet = true, CaptureLayoutVersion = 2, CaptureX = .7, CaptureY = .8, CaptureWidth = .2, CaptureHeight = .1, Monitor = "test-monitor" };
            var kinds = new[] { WeaponKind.Ordinary, WeaponKind.Shotgun, WeaponKind.Sniper };
            foreach (WeaponKind kind in kinds)
            {
                CrosshairStyle current = config.Profile(kind);
                current.Shape = kind == WeaponKind.Ordinary ? 6 : kind == WeaponKind.Shotgun ? 5 : 4;
                current.CenterDot = kind == WeaponKind.Shotgun; current.CustomParts = 1 << (int)kind; current.DiagonalAngle = 30 + (int)kind * 15;
                config.SavePreset("保持原样", current, kind);
            }
            CrosshairStyle source = config.Profile(sourceKind);
            source.Length = 25.75; source.Gap = 7.25; source.Thickness = 1.35; source.Outline = false;
            source.ColorIndex = 4; source.ColorHex = sourceKind == WeaponKind.Ordinary ? "" : "#12ABCD";
            CrosshairStyle[] before = kinds.Select(k => config.Profile(k).Copy(config.Profile(k).Shape)).ToArray();
            config.SyncCommonParameters(sourceKind);
            Assert(kinds.All(k => config.Profile(k).Length == 25.75 && config.Profile(k).Gap == 7.25 && config.Profile(k).Thickness == 1.35
                && !config.Profile(k).Outline && config.Profile(k).ColorIndex == 4 && config.Profile(k).ColorHex == source.ColorHex), "Common appearance copies from " + sourceKind);
            Assert(kinds.All(k => config.Profile(k).Shape == before[(int)k].Shape && config.Profile(k).CustomParts == before[(int)k].CustomParts
                && config.Profile(k).CenterDot == before[(int)k].CenterDot && config.Profile(k).DiagonalAngle == before[(int)k].DiagonalAngle), "Sync preserves each category geometry from " + sourceKind);
            Assert(kinds.All(k => config.Presets(k)[0].Style.Length == 8) && source.SameAppearance(before[(int)sourceKind]), "Sync preserves saved presets and source from " + sourceKind);
            Settings restored = RoundTrip(config);
            Assert(kinds.All(k => restored.Profile(k).SameAppearance(config.Profile(k))) && restored.AutoDetect && restored.OcrLanguage == "en-US" && restored.ScanInterval == 2000
                && restored.CaptureRegionSet && restored.CaptureX == .7 && restored.CaptureY == .8 && restored.CaptureWidth == .2 && restored.CaptureHeight == .1 && restored.Monitor == "test-monitor",
                "Sync persists appearance without changing recognition or monitor from " + sourceKind);
        }
        var uiConfig = new Settings(); uiConfig.Profile(WeaponKind.Sniper).Length = 19.25;
        using (var overlay = new Overlay(uiConfig, false))
        using (var window = new TestSettingsWindow(overlay))
        {
            window.Show(); Application.DoEvents();
            Descendants(window).OfType<ComboBox>().Single(c => c.Name == "EditingProfile").SelectedIndex = 2;
            Descendants(window).OfType<Button>().Single(b => b.Name == "SyncCommonParameters").PerformClick();
            Assert(uiConfig.Length == 19.25 && uiConfig.Shotgun.Length == 19.25 && uiConfig.Shotgun.Shape == 3 && uiConfig.Sniper.Shape == 4,
                "Sync button uses editing category instead of active ordinary overlay");
            Assert(Descendants(window).OfType<Label>().Any(l => l.Text.StartsWith("已将狙击枪")), "Sync button confirms source category");
            uiConfig.Sniper.Length = 20;
            Assert(uiConfig.Length == 19.25 && uiConfig.Shotgun.Length == 19.25, "Sync is a one-time copy and later changes remain independent");
            window.Close();
        }
    }

    static void DecimalPresetChecks()
    {
        var settings = new Settings { Length = 12.75, Gap = 3.25, Thickness = 1.25, ColorHex = "#ABCDEF", Outline = false };
        settings.Validate();
        CustomPreset preset = settings.SavePreset(" 自定义 A ", settings);
        settings.Thickness = 3;
        Assert(preset.Name == "自定义 A" && preset.Style.Thickness == 1.25, "Preset captures a named independent snapshot");
        settings.SavePreset("自定义 a", settings);
        Assert(settings.CustomPresets.Count == 1 && preset.Style.Thickness == 3, "Saving an existing name updates without duplicate entries");
        bool rejected = false; try { settings.SavePreset(" ", settings); } catch (ArgumentException) { rejected = true; }
        Assert(rejected, "Blank preset names are rejected at persistence boundary");
        settings.Thickness = 1.25; settings.SavePreset("第二套", settings);
        var serializer = new XmlSerializer(typeof(Settings));
        using (var output = new StringWriter())
        {
            serializer.Serialize(output, settings);
            using (var input = new StringReader(output.ToString()))
            {
                var restored = (Settings)serializer.Deserialize(input); restored.Validate();
                Assert(restored.Length == 12.75 && restored.Gap == 3.25 && restored.Thickness == 1.25 && restored.CustomPresets.Count == 2 && restored.CustomPresets[1].Style.Thickness == 1.25, "Fractional profiles and named presets survive reload");
                restored.CustomPresets.RemoveAt(0);
                using (var deleted = new StringWriter())
                {
                    serializer.Serialize(deleted, restored);
                    using (var input2 = new StringReader(deleted.ToString()))
                    {
                        var saved = (Settings)serializer.Deserialize(input2);
                        Assert(saved.CustomPresets.Count == 1 && saved.CustomPresets[0].Name == "第二套" && saved.Thickness == 1.25, "Preset deletion survives reload and preserves active appearance");
                    }
                }
            }
        }
        using (Bitmap fractional = Renderer.Rasterize(settings, 128, 128))
        {
            bool partial = false;
            for (int y = 0; y < fractional.Height; y++) for (int x = 0; x < fractional.Width; x++)
            { int alpha = fractional.GetPixel(x, y).A; if (alpha > 0 && alpha < 255) partial = true; }
            Assert(partial && fractional.GetPixel(0, 0).A == 0, "Fractional geometry produces alpha coverage and transparent background");
        }
        settings.Length = 8; settings.Gap = 4; settings.Thickness = 2;
        using (Bitmap integral = Renderer.Rasterize(settings, 128, 128))
        {
            Assert(integral.GetPixel(64, 56).ToArgb() == settings.ForegroundColor.ToArgb() && integral.GetPixel(64, 64).A == 0, "Integer crosshair retains crisp color and transparent gap");
        }
        settings.Length = double.NaN; settings.Thickness = double.PositiveInfinity; settings.Gap = -20; settings.Validate();
        Assert(settings.Length == 8 && settings.Thickness == 2 && settings.Gap == 0, "Nonfinite and invalid dimensions safely normalize");
    }

    static void CustomPartChecks()
    {
        var style = new Settings { Shape = 5, Length = 12, Gap = 4, Thickness = 2, Outline = false,
            CustomParts = CrosshairParts.Top | CrosshairParts.Right, ColorHex = "#33DDCC" };
        using (Bitmap bitmap = Renderer.Rasterize(style, 128, 128))
        {
            Assert(bitmap.GetPixel(64, 55).A == 255 && bitmap.GetPixel(73, 64).A == 255, "Enabled top and right elements render");
            Assert(bitmap.GetPixel(64, 73).A == 0 && bitmap.GetPixel(55, 64).A == 0, "Disabled bottom and left elements stay transparent");
        }
        style.CustomParts = 0;
        using (Bitmap bitmap = Renderer.Rasterize(style, 128, 128))
        {
            bool blank = true;
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) blank &= bitmap.GetPixel(x, y).A == 0;
            Assert(blank, "Turning off every element produces a fully transparent crosshair");
        }
        style.CenterDot = true;
        using (Bitmap bitmap = Renderer.Rasterize(style, 128, 128)) Assert(bitmap.GetPixel(64, 64).A == 255, "Center dot works independently of line elements");
        style.CenterDot = false; style.CustomParts = CrosshairParts.Ring;
        using (Bitmap bitmap = Renderer.Rasterize(style, 128, 128)) Assert(bitmap.GetPixel(76, 64).A == 255 && bitmap.GetPixel(64, 64).A == 0, "Ring can be enabled on its own");
        style.Shape = 6; style.Length = 16;
        using (Bitmap bitmap = Renderer.Rasterize(style, 128, 128))
        {
            Assert(bitmap.GetPixel(64, 54).A > 0 && bitmap.GetPixel(74, 70).A > 0 && bitmap.GetPixel(54, 70).A > 0, "Mercedes has upper spoke and symmetric lower spokes");
            Assert(bitmap.GetPixel(64, 76).A == 0 && bitmap.GetPixel(74, 58).A == 0, "Mercedes excludes vertical bottom and upper diagonals");
        }
        style.Shape = 7;
        using (Bitmap bitmap = Renderer.Rasterize(style, 128, 128))
            Assert(bitmap.GetPixel(73, 73).A > 0 && bitmap.GetPixel(55, 55).A > 0 && bitmap.GetPixel(73, 55).A > 0 && bitmap.GetPixel(55, 73).A > 0, "X template renders all four diagonals");
        style.Shape = 5; style.DiagonalAngle = 30; style.CustomParts = CrosshairParts.Top | CrosshairParts.BottomRight | CrosshairParts.Ring;
        style.SavePreset("单侧三叉", style);
        var serializer = new XmlSerializer(typeof(Settings));
        using (var text = new StringWriter())
        {
            serializer.Serialize(text, style);
            using (var input = new StringReader(text.ToString()))
            {
                var restored = (Settings)serializer.Deserialize(input); restored.Validate();
                Assert(restored.Shape == 5 && restored.CustomParts == style.CustomParts && restored.DiagonalAngle == 30
                    && restored.CustomPresets[0].Style.CustomParts == style.CustomParts && restored.CustomPresets[0].Style.DiagonalAngle == 30,
                    "Custom part switches, angles and saved preset survive reload");
            }
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
            await WaitFor(delegate { return overlay.Recognition.DisplayShape == 0 && overlay.Recognition.Status.StartsWith("只读到暗色枪名"); }, "Only dim reserve name remains: restore ordinary style");
            config.OcrLanguage = "zh-Hans-CN"; overlay.Changed(); gun = "哨兵"; game.Invalidate();
            await WaitFor(delegate { return overlay.Recognition.DisplayShape == 4; }, "Chinese HUD capture selects sniper");
            config.AutoDetect = false; overlay.Changed();
            await Task.Delay(650); Assert(overlay.Recognition.Status == "自动识别已关闭", "Disable stops recognition");
            config.ColorHex = "#010203"; overlay.Changed();
            Assert(overlay.TransparencyKey.ToArgb() != config.ForegroundColor.ToArgb(), "Custom RGB remains independent of transparency");
            using (var settings = new TestSettingsWindow(overlay))
            {
                settings.Show(); await Task.Delay(150);
                SliderRow[] sliders = Descendants(settings).OfType<SliderRow>().ToArray();
                Assert(sliders.Length == 3, "All three numeric appearance controls replaced by sliders");
                sliders[0].Value = 19; sliders[1].Value = 7; sliders[2].Value = 4;
                Assert(config.Length == 19 && config.Gap == 7 && config.Thickness == 4, "Sliders update live settings");
                TextBox hex = Descendants(settings).OfType<TextBox>().Single(c => c.Name == "ColorHex"); hex.Text = "#AA33CC";
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
        try { UnitChecks(); ScopedPresetChecks(); ScopedPresetUiChecks(); CommonParameterChecks(); DecimalPresetChecks(); CustomPartChecks(); ImagePipeline(); HighlightChecks(); NumericHudChecks(); ProfileChecks(); CaptureRestrictionChecks(); UiChecks(); } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
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

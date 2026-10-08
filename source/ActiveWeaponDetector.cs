using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenCrosshair
{
    public sealed class WeaponLabel
    {
        public Weapon Weapon;
        public RectangleF Bounds;
        public double Brightness;
        public double Contrast;
    }

    public sealed class ActiveWeaponReading
    {
        public Weapon Active;
        public string Message;
        public readonly List<WeaponLabel> Labels = new List<WeaponLabel>();
    }

    public static class ActiveWeaponDetector
    {
        public static ActiveWeaponReading Read(Bitmap original, OcrReading text)
        {
            var result = new ActiveWeaponReading();
            // OCR can use a contrast-enhanced copy, but activation must always use the original HUD colors.
            byte[] pixels = new byte[original.Width * original.Height * 4];
            BitmapData data = original.LockBits(new Rectangle(Point.Empty, original.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < original.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * original.Width * 4, original.Width * 4); }
            finally { original.UnlockBits(data); }

            foreach (OcrLineBox line in text.Lines)
                for (int start = 0; start < line.Words.Count; start++)
                {
                    var span = new StringBuilder();
                    for (int end = start; end < Math.Min(line.Words.Count, start + 12); end++)
                    {
                        span.Append(line.Words[end].Text);
                        Weapon weapon = WeaponCatalog.Match(span.ToString());
                        if (weapon == null) continue;
                        // Use the shortest matching word span; slot numbers are permanently bright and must be excluded.
                        if (end > start)
                        {
                            var withoutFirst = new StringBuilder();
                            for (int k = start + 1; k <= end; k++) withoutFirst.Append(line.Words[k].Text);
                            if (WeaponCatalog.Match(withoutFirst.ToString()) == weapon) break;
                        }
                        var brightness = new List<double>(); var contrast = new List<double>();
                        RectangleF bounds = line.Words[start].Bounds;
                        for (int k = start; k <= end; k++)
                        {
                            bounds = RectangleF.Union(bounds, line.Words[k].Bounds);
                            double glyphBrightness, glyphContrast;
                            Measure(pixels, original.Size, line.Words[k].Bounds, out glyphBrightness, out glyphContrast);
                            brightness.Add(glyphBrightness); contrast.Add(glyphContrast);
                        }
                        brightness.Sort(); contrast.Sort();
                        if (!result.Labels.Any(delegate(WeaponLabel existing) { return existing.Bounds.IntersectsWith(bounds); }))
                            result.Labels.Add(new WeaponLabel { Weapon = weapon, Bounds = bounds, Brightness = brightness[brightness.Count / 2], Contrast = contrast[contrast.Count / 2] });
                        break;
                    }
                }
            List<WeaponLabel> ranked = result.Labels.OrderByDescending(delegate(WeaponLabel label) { return label.Brightness; }).ToList();
            if (ranked.Count == 0) { result.Message = "未读到枪名，请把两个枪名一起框入"; return result; }
            WeaponLabel winner = ranked[0];
            // A readable dim label is not an active gun. A tie or a fading HUD must not select a slot arbitrarily.
            if (winner.Brightness < 210 || winner.Contrast < 35 || (ranked.Count > 1 && winner.Brightness - ranked[1].Brightness < 25))
            { result.Message = "枪名高亮不明确，等待画面稳定"; return result; }
            result.Active = winner.Weapon;
            result.Message = "高亮：" + winner.Weapon.Name;
            return result;
        }

        static void Measure(byte[] pixels, Size size, RectangleF word, out double brightness, out double contrast)
        {
            Rectangle bounds = Rectangle.Intersect(Rectangle.Ceiling(word), new Rectangle(Point.Empty, size));
            int[] light = new int[256], all = new int[256]; int eligible = 0, total = 0;
            for (int y = bounds.Top; y < bounds.Bottom; y++)
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    int p = (y * size.Width + x) * 4;
                    int b = pixels[p], g = pixels[p + 1], r = pixels[p + 2];
                    int luminance = (b * 29 + g * 150 + r * 77) >> 8;
                    all[luminance]++; total++;
                    // White/gray text is distinct from Apex's colored slot background. Ignore that background hue.
                    if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 45 && luminance >= 60)
                    { light[luminance]++; eligible++; }
                }
            brightness = eligible < Math.Max(4, total / 100) ? 0 : Percentile(light, eligible, .80);
            contrast = brightness - Percentile(all, total, .25);
        }

        static int Percentile(int[] histogram, int count, double fraction)
        {
            if (count == 0) return 0;
            int target = Math.Max(1, (int)Math.Ceiling(count * fraction)), cumulative = 0;
            for (int i = 0; i < histogram.Length; i++) { cumulative += histogram[i]; if (cumulative >= target) return i; }
            return 255;
        }
    }
}

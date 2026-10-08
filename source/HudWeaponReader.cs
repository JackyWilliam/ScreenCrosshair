using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScreenCrosshair
{
    public static class HudWeaponReader
    {
        public static ActiveWeaponReading Read(Bitmap crop, WindowsOcr ocr, string language)
        {
            double scale = Math.Min(3.0, Math.Min(Math.Max(1.0, 96.0 / crop.Height), 1600.0 / crop.Width));
            ActiveWeaponReading original;
            using (Bitmap enlarged = Enlarge(crop, scale))
                original = ActiveWeaponDetector.Read(enlarged, ocr.ReadLayout(enlarged, language));

            // A large crop leaves short numeric names tiny; Chinese OCR can return text but omit 30-30.
            // Never discard two known labels just because their brightness is ambiguous in this frame.
            if (original.Active != null || original.Labels.Count >= 2) return original;
            using (Bitmap enlarged = Enlarge(crop, Math.Min(3.0, 1600.0 / crop.Width)))
            {
                OcrReading enhanced = ocr.ReadLayout(enlarged, language, true);
                // Only OCR sees the black/white copy. Activation must use the original HUD brightness.
                ActiveWeaponReading retry = ActiveWeaponDetector.Read(enlarged, enhanced);
                retry.RawText = "原图：" + original.RawText + Environment.NewLine + "增强：" + enhanced.Text;
                return retry;
            }
        }

        static Bitmap Enlarge(Bitmap crop, double scale)
        {
            var enlarged = new Bitmap(Math.Max(1, (int)(crop.Width * scale)), Math.Max(1, (int)(crop.Height * scale)), PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(enlarged))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(crop, new Rectangle(Point.Empty, enlarged.Size));
                }
                return enlarged;
            }
            catch { enlarged.Dispose(); throw; }
        }
    }
}

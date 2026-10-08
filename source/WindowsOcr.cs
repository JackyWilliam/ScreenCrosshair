using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ScreenCrosshair
{
    public sealed class WindowsOcr
    {
        readonly Type engineType, bitmapType, resultType;
        readonly MethodInfo asBuffer, asTask;
        object engine;
        string engineLanguage;

        static Type WinRt(string name) { return Type.GetType(name + ", Windows.Foundation, ContentType=WindowsRuntime", true); }

        public WindowsOcr()
        {
            // .NET Framework projects WinRT at runtime; this avoids shipping an SDK or an OCR service.
            engineType = WinRt("Windows.Media.Ocr.OcrEngine");
            bitmapType = WinRt("Windows.Graphics.Imaging.SoftwareBitmap");
            resultType = WinRt("Windows.Media.Ocr.OcrResult");
            Assembly bridge = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            asBuffer = bridge.GetType("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions").GetMethod("AsBuffer", new Type[] { typeof(byte[]) });
            foreach (MethodInfo method in bridge.GetType("System.WindowsRuntimeSystemExtensions").GetMethods())
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "AsTask" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && parameters.Length == 1 && parameters[0].ParameterType.Name == "IAsyncOperation`1")
                { asTask = method.MakeGenericMethod(resultType); break; }
            }
            if (asTask == null || asBuffer == null) throw new InvalidOperationException("Windows OCR 运行库不可用。");
        }

        public string Read(Bitmap input, string language)
        {
            if (engine == null || engineLanguage != language)
            {
                object selected = null;
                foreach (object candidate in (IEnumerable)engineType.GetProperty("AvailableRecognizerLanguages").GetValue(null))
                {
                    string tag = (string)candidate.GetType().GetProperty("LanguageTag").GetValue(candidate);
                    if (tag.Equals(language, StringComparison.OrdinalIgnoreCase)) { selected = candidate; break; }
                }
                if (selected == null) throw new InvalidOperationException("缺少 " + language + " 的 Windows 文字识别组件，请在系统语言设置中安装，或切换识别语言。");
                engine = engineType.GetMethod("TryCreateFromLanguage").Invoke(null, new object[] { selected });
                if (engine == null) throw new InvalidOperationException("无法启动 Windows 文字识别。");
                engineLanguage = language;
            }
            byte[] pixels = new byte[input.Width * input.Height * 4];
            BitmapData data = input.LockBits(new Rectangle(0, 0, input.Width, input.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int row = 0; row < input.Height; row++) Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), pixels, row * input.Width * 4, input.Width * 4);
            }
            finally { input.UnlockBits(data); }
            object format = Enum.Parse(WinRt("Windows.Graphics.Imaging.BitmapPixelFormat"), "Bgra8");
            object alpha = Enum.Parse(WinRt("Windows.Graphics.Imaging.BitmapAlphaMode"), "Ignore");
            object bitmap = Activator.CreateInstance(bitmapType, new object[] { format, input.Width, input.Height, alpha });
            try
            {
                bitmapType.GetMethod("CopyFromBuffer").Invoke(bitmap, new object[] { asBuffer.Invoke(null, new object[] { pixels }) });
                string text = Recognize(bitmap);
                if (!string.IsNullOrWhiteSpace(text)) return text;
                // Sparse bright HUD glyphs on a tinted background can be missed; retry once at high contrast.
                // Keep the original pass first so colored or antialiased names are not discarded by thresholding.
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    int luminance = (pixels[i] * 29 + pixels[i + 1] * 150 + pixels[i + 2] * 77) >> 8;
                    byte value = (byte)(luminance >= 160 ? 0 : 255);
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = value; pixels[i + 3] = 255;
                }
                bitmapType.GetMethod("CopyFromBuffer").Invoke(bitmap, new object[] { asBuffer.Invoke(null, new object[] { pixels }) });
                return Recognize(bitmap);
            }
            finally { ((IDisposable)bitmap).Dispose(); }
        }

        string Recognize(object bitmap)
        {
            object operation = engineType.GetMethod("RecognizeAsync").Invoke(engine, new object[] { bitmap });
            Task task = (Task)asTask.Invoke(null, new object[] { operation });
            task.GetAwaiter().GetResult();
            object result = task.GetType().GetProperty("Result").GetValue(task);
            return (string)resultType.GetProperty("Text").GetValue(result);
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ScreenCrosshair;

// Compile with source/Theme.cs and the WinForms/Drawing references; pass the destination .ico as the only argument.
// PNG-backed icon frames keep the same vector-drawn mark sharp from the notification area to Explorer's large icons.
public static class GenerateIcon
{
    public static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };
        var frames = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (Bitmap bitmap = AppArtwork.CreateBitmap(sizes[i], true))
            using (var stream = new MemoryStream()) { bitmap.Save(stream, ImageFormat.Png); frames[i] = stream.ToArray(); }
        using (var output = new BinaryWriter(File.Create(args[0])))
        {
            output.Write((short)0); output.Write((short)1); output.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                output.Write((byte)0); output.Write((byte)0); output.Write((short)1); output.Write((short)32);
                output.Write(frames[i].Length); output.Write(offset); offset += frames[i].Length;
            }
            foreach (byte[] frame in frames) output.Write(frame);
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Astonia.Editor
{
    public static class SpriteEnhancer
    {
        private static int[] Double(int[] input, int width, int height)
        {
            int[] output = new int[width * height * 4]; int stride = width * 2;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int center = input[y * width + x], up = input[Math.Max(0, y - 1) * width + x], down = input[Math.Min(height - 1, y + 1) * width + x], left = input[y * width + Math.Max(0, x - 1)], right = input[y * width + Math.Min(width - 1, x + 1)], index = y * 2 * stride + x * 2;
                bool edge = up != down && left != right;
                output[index] = edge && left == up ? left : center; output[index + 1] = edge && up == right ? right : center; output[index + stride] = edge && left == down ? left : center; output[index + stride + 1] = edge && down == right ? right : center;
            }
            return output;
        }
        public static Bitmap Enlarge(Bitmap source)
        {
            var original = new int[source.Width * source.Height];
            BitmapData read = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < source.Height; y++) Marshal.Copy(IntPtr.Add(read.Scan0, y * read.Stride), original, y * source.Width, source.Width); }
            finally { source.UnlockBits(read); }
            int[] twice = Double(original, source.Width, source.Height), four = Double(twice, source.Width * 2, source.Height * 2); int width = source.Width * 4, height = source.Height * 4;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            { int pixel = original[(y / 4) * source.Width + x / 4], index = y * width + x; if ((four[index] & unchecked((int)0xff000000)) != (pixel & unchecked((int)0xff000000))) four[index] = pixel; }
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb); BitmapData write = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < height; y++) Marshal.Copy(four, y * width, IntPtr.Add(write.Scan0, y * write.Stride), width); }
            finally { result.UnlockBits(write); }
            return result;
        }
    }
}

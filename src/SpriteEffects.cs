using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Astonia.Editor
{
    public sealed class SpriteEffects
    {
        public int Cr, Cg, Cb, Light, Saturation, C1, C2, C3, Shine;
        public bool Active { get { return Cr != 0 || Cg != 0 || Cb != 0 || Light != 0 || Saturation != 0 || C1 != 0 || C2 != 0 || C3 != 0 || Shine != 0; } }
        private string key;
        public string Key { get { return key ?? (key = Cr + ":" + Cg + ":" + Cb + ":" + Light + ":" + Saturation + ":" + C1 + ":" + C2 + ":" + C3 + ":" + Shine); } }
        public static SpriteEffects From(IDictionary<string, object> row)
        { return new SpriteEffects { Cr = SpriteRules.Number(row, "cr"), Cg = SpriteRules.Number(row, "cg"), Cb = SpriteRules.Number(row, "cb"), Light = SpriteRules.Number(row, "light"), Saturation = SpriteRules.Number(row, "saturation"), C1 = SpriteRules.Number(row, "c1"), C2 = SpriteRules.Number(row, "c2"), C3 = SpriteRules.Number(row, "c3"), Shine = SpriteRules.Number(row, "shine") }; }
        private static int Clamp(double value) { return Math.Max(0, Math.Min(255, (int)value)); }
        private static int Gloss(int channel, int amount) { double value = channel / 127.5; return Clamp((Math.Pow(value, 4) * amount + value * (100 - amount)) * 255 / 200); }
        private int Mix(double source, int shift, double one, double two, double three)
        { return Clamp(16 * (one * ((C1 >> shift) & 31) + two * ((C2 >> shift) & 31) + three * ((C3 >> shift) & 31)) + 248 * Math.Max(0, source)); }
        public int Transform(int pixel)
        {
            int r = (pixel >> 16) & 255, g = (pixel >> 8) & 255, b = pixel & 255, alpha = (int)((uint)pixel >> 24);
            if (alpha == 0 || r == 255 && g == 0 && b == 255) return 0;
            if (C1 != 0 || C2 != 0 || C3 != 0)
            {
                double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0, one = 0, two = 0, three = 0, gloss = 0, largest = Math.Max(rr, Math.Max(gg, bb)) + 0.000001;
                if (C1 != 0 && gg / largest > 0.99 && rr / largest < 0.7 && bb / largest < 0.7) { one = gg - Math.Max(bb, rr); if ((C1 & 0x8000) != 0) gloss += gg / largest - Math.Max(rr / largest, bb / largest); gg -= one; }
                largest = Math.Max(rr, Math.Max(gg, bb)) + 0.000001;
                if (C2 != 0 && bb / largest > 0.99 && rr / largest < 0.7 && gg / largest < 0.7) { two = bb - Math.Max(rr, gg); if ((C2 & 0x8000) != 0) gloss += bb / largest - Math.Max(rr / largest, gg / largest); bb -= two; }
                largest = Math.Max(rr, Math.Max(gg, bb)) + 0.000001;
                if (C3 != 0 && rr / largest > 0.99 && gg / largest < 0.4 && bb / largest < 0.4) { three = rr - Math.Max(gg, bb); if ((C3 & 0x8000) != 0) gloss += rr / largest - Math.Max(gg / largest, bb / largest); rr -= three; }
                r = Mix(rr, 10, one, two, three); g = Mix(gg, 5, one, two, three); b = Mix(bb, 0, one, two, three);
                if (gloss > 0.1) { int amount = (int)(gloss * 50); r = Gloss(r, amount); g = Gloss(g, amount); b = Gloss(b, amount); }
            }
            if (Light != 0 || Saturation != 0 || Cr != 0 || Cg != 0 || Cb != 0)
            {
                r += Light; g += Light; b += Light;
                if (Saturation != 0) { int gray = (r + g + b) / 3; r = (r * (20 - Saturation) + gray * Saturation) / 20; g = (g * (20 - Saturation) + gray * Saturation) / 20; b = (b * (20 - Saturation) + gray * Saturation) / 20; }
                int cr = (int)(Cr * 0.75), cg = (int)(Cg * 0.75), cb = (int)(Cb * 0.75);
                r = Math.Max(0, r + cr - cg / 2 - cb / 2); g = Math.Max(0, g + cg - cr / 2 - cb / 2); b = Math.Max(0, b + cb - cr / 2 - cg / 2);
                if (r > 255) { g += (r - 255) / 2; b += (r - 255) / 2; r = 255; } if (g > 255) { r += (g - 255) / 2; b += (g - 255) / 2; g = 255; } if (b > 255) { r += (b - 255) / 2; g += (b - 255) / 2; b = 255; }
                r = Clamp(r); g = Clamp(g); b = Clamp(b);
            }
            if (Shine != 0) { r = Gloss(r, Shine); g = Gloss(g, Shine); b = Gloss(b, Shine); }
            return unchecked((alpha << 24) | (r << 16) | (g << 8) | b);
        }
        public void Apply(Bitmap image)
        {
            BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb); var row = new int[image.Width];
            try { for (int y = 0; y < image.Height; y++) { IntPtr pointer = IntPtr.Add(data.Scan0, y * data.Stride); Marshal.Copy(pointer, row, 0, row.Length); for (int x = 0; x < row.Length; x++) row[x] = Transform(row[x]); Marshal.Copy(row, 0, pointer, row.Length); } }
            finally { image.UnlockBits(data); }
        }
    }
}

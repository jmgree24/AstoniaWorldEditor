using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Astonia.Editor
{
    public static class TiledMapExport
    {
        public static void Write(MapCanvas source, string path, int width, int height, float zoom, float cameraX, float cameraY, bool smooth, int graphicsScale, bool title = false, float titleScale = 1)
        {
            const int frameWidth = 1024, bandHeight = 128, overlap = 32;
            string full = Path.GetFullPath(path), temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = new PngStitchWriter(temp, width, height))
                    for (int top = 0; top < height; top += bandHeight)
                    {
                        int rows = Math.Min(bandHeight, height - top); var band = new byte[checked(width * rows * 4)];
                        for (int left = 0; left < width; left += frameWidth)
                        {
                            int columns = Math.Min(frameWidth, width - left);
                            using (MapCanvas frame = source.CleanRenderFrame(columns + overlap * 2, rows + overlap * 2, zoom, cameraX, cameraY, smooth, graphicsScale))
                            {
                                frame.RenderOrigin = new Point(left - overlap, top - overlap);
                                using (Bitmap image = frame.RenderImage())
                                {
                                if (title) using (Graphics graphics = Graphics.FromImage(image)) DrawTitles(graphics, source.Document.Name, width, height, left - overlap, top - overlap, titleScale);
                                BitmapData data = image.LockBits(new Rectangle(overlap, overlap, columns, rows), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                                try { for (int row = 0; row < rows; row++) Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), band, (row * width + left) * 4, columns * 4); }
                                finally { image.UnlockBits(data); }
                                }
                            }
                        }
                        for (int row = 0; row < rows; row++) writer.WriteBgraRow(band, row * width * 4);
                    }
                if (File.Exists(full)) File.Replace(temp, full, null); else File.Move(temp, full);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private static void DrawTitles(Graphics graphics, string mapName, int width, int height, int left, int top, float units)
        {
            using (var title = new Font("Segoe UI", 28 * units, FontStyle.Bold)) using (var sub = new Font("Segoe UI", 15 * units)) using (var brush = new SolidBrush(Theme.Gold)) using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                graphics.DrawString(Path.GetFileNameWithoutExtension(mapName), title, brush, new RectangleF(-left, 24 * units - top, width, 55 * units), format);
                graphics.DrawString("MAP OVERVIEW · lowered walls reveal paths", sub, brush, new RectangleF(-left, 83 * units - top, width, 35 * units), format);
                graphics.DrawString("Astonia World Editor · based on the Astonia 3 engine by Daniel Brockhaus", sub, brush, new RectangleF(-left, height - 45 * units - top, width, 35 * units), format);
            }
        }
        public static void Overview(MapDocument document, AssetCatalog assets, string path, int width, bool smooth, bool finestArtwork, int nativeScale)
        {
            if (nativeScale == 0 && width != 4096 && width != 8192 && width != 16384 || nativeScale != 0 && nativeScale != 1 && nativeScale != 2 && nativeScale != 4) throw new ArgumentOutOfRangeException("width");
            int[] fields = Enumerable.Range(0, document.Tiles.Length).Where(index => { Tile tile = document.Tiles[index]; return tile.Ground != 0 || tile.Foreground != 0 || tile.Item != "" || tile.Character != ""; }).ToArray();
            if (fields.Length == 0) throw new IOException("This map has no artwork to snapshot.");
            int minX = fields.Min(i => i % 256), maxX = fields.Max(i => i % 256), minY = fields.Min(i => i / 256), maxY = fields.Max(i => i / 256), span = maxX - minX + maxY - minY + 2;
            float units = nativeScale > 0 ? nativeScale : width / 4096f;
            if (nativeScale > 0) width = (int)Math.Ceiling(span * 20 * nativeScale + 340 * units);
            float zoom = nativeScale > 0 ? nativeScale : Math.Min(8, (width - 340 * units) / (span * 20f)); int height = (int)Math.Ceiling(span * 10 * zoom + 410 * units);
            float cameraX = width / 2f - (minX + maxX - minY - maxY) * 10 * zoom, cameraY = 240 * units - (minX + minY) * 10 * zoom;
            using (var source = new MapCanvas { Document = document, Assets = assets, ShowNpcs = true }) Write(source, path, width, height, zoom, cameraX, cameraY, smooth, finestArtwork ? 4 : 0, true, units);
        }
    }
}

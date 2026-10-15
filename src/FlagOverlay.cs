using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace Astonia.Editor
{
    public sealed class FlagOverlay
    {
        public HashSet<string> Visible = new HashSet<string>(new[] { "MF_MOVEBLOCK", "MF_SIGHTBLOCK", "MF_INDOORS" }, StringComparer.OrdinalIgnoreCase);
        public string[] DisplayFlags(Tile tile, bool enabled, bool preview)
        { return tile.Flags.Where(f => !f.Equals("MF_NEUTRAL", StringComparison.OrdinalIgnoreCase) && (preview || (enabled && Visible.Contains(f)))).ToArray(); }
        public void Draw(Graphics graphics, Tile tile, PointF center, float zoom, bool enabled, bool preview = false, string prefix = "")
        {
            string[] flags = DisplayFlags(tile, enabled, preview); bool water = tile.Flags.Contains("MF_UNDERWATER");
            if (flags.Length == 0 && !water) return;
            var diamond = new[] { new PointF(center.X, center.Y - 10 * zoom), new PointF(center.X + 20 * zoom, center.Y), new PointF(center.X, center.Y + 10 * zoom), new PointF(center.X - 20 * zoom, center.Y) };
            Color color = EditorFlags.Find(water ? "MF_UNDERWATER" : flags[0]).Color;
            using (var brush = new SolidBrush(Color.FromArgb(water ? (preview ? 166 : 122) : 82, color))) graphics.FillPolygon(brush, diamond);
            if (water && flags.Contains("MF_UNDERWATER")) using (var pen = new Pen(ColorTranslator.FromHtml("#55c9ff"), preview ? 2.5f : 1.5f)) graphics.DrawPolygon(pen, diamond);
            int rows = (flags.Length + 2) / 3;
            for (int row = 0; row < rows; row++) DrawLabel(graphics, prefix + string.Join(" ", flags.Skip(row * 3).Take(3).Select(f => EditorFlags.Find(f).Identifier)), new PointF(center.X, center.Y + (row - (rows - 1) / 2f) * 8 * zoom), Math.Max(7, 7 * zoom), 36 * zoom, Math.Max(2, zoom * 2), Color.White, Color.FromArgb(16, 20, 22));
        }
        public static void DrawLabel(Graphics graphics, string text, PointF center, float pixels, float maxWidth, float outline, Color fill, Color stroke)
        {
            GraphicsState state = graphics.Save(); graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var family = new FontFamily("Consolas")) using (var path = new GraphicsPath())
            {
                path.AddString(text, family, (int)FontStyle.Bold, pixels, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF bounds = path.GetBounds(); if (bounds.Width <= 0) { graphics.Restore(state); return; }
                using (var matrix = new Matrix())
                {
                    float scaleX = Math.Min(1, maxWidth / bounds.Width);
                    matrix.Translate(center.X, center.Y); matrix.Scale(scaleX, 1); matrix.Translate(-bounds.X - bounds.Width / 2, -bounds.Y - bounds.Height / 2); path.Transform(matrix);
                }
                using (var pen = new Pen(stroke, outline) { LineJoin = LineJoin.Round }) graphics.DrawPath(pen, path);
                using (var brush = new SolidBrush(fill)) graphics.FillPath(brush, path);
            }
            graphics.Restore(state);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Astonia.Editor
{
    public sealed class SpriteGrid : ScrollableControl
    {
        public AssetCatalog Assets;
        public List<int> Sprites = new List<int>();
        public int SelectedId;
        public bool RawImages;
        public event Action<int> Picked;
        private int columns = 3, cellWidth = 72;
        public SpriteGrid() { AutoScroll = true; DoubleBuffered = true; BackColor = Theme.Panel; Cursor = Cursors.Hand; }
        public void SetSprites(List<int> ids)
        {
            Sprites = ids; AutoScrollPosition = Point.Empty; Calculate(); Invalidate();
        }
        private void Calculate() { columns = Math.Max(1, (Width - 20) / 72); cellWidth = Math.Max(60, (Width - 20) / columns); AutoScrollMinSize = new Size(0, (int)Math.Ceiling(Sprites.Count / (double)columns) * 82); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Calculate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            int offset = AutoScrollPosition.Y;
            int start = Math.Max(0, -offset / 82 * columns), end = Math.Min(Sprites.Count, ((Height - offset) / 82 + 1) * columns);
            for (int i = start; i < end; i++)
            {
                int id = Sprites[i]; var rect = new Rectangle((i % columns) * cellWidth + 3, (i / columns) * 82 + offset + 3, cellWidth - 6, 76);
                using (var brush = new SolidBrush(id == SelectedId ? Theme.Active : Theme.Control)) g.FillRectangle(brush, rect);
                using (var pen = new Pen(id == SelectedId ? Theme.Gold : Color.FromArgb(62, 62, 73))) g.DrawRectangle(pen, rect);
                SpriteArt art = Assets == null ? null : RawImages ? Assets.Art(id) : Assets.PreviewArt(id);
                if (art != null)
                {
                    float scale = Math.Min((rect.Width - 10f) / art.Image.Width, 49f / art.Image.Height);
                    int w = Math.Max(1, (int)(art.Image.Width * scale)), h = Math.Max(1, (int)(art.Image.Height * scale));
                    g.DrawImage(art.Image, new Rectangle(rect.X + (rect.Width - w) / 2, rect.Y + 4 + (49 - h) / 2, w, h));
                }
                TextRenderer.DrawText(g, id.ToString(), Font, new Rectangle(rect.X, rect.Bottom - 20, rect.Width, 18), Theme.Text, TextFormatFlags.HorizontalCenter);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); int col = e.X / cellWidth, row = (e.Y - AutoScrollPosition.Y) / 82, index = row * columns + col;
            if (col < columns && index >= 0 && index < Sprites.Count) { SelectedId = Sprites[index]; Invalidate(); if (Picked != null) Picked(SelectedId); }
        }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
    }
}

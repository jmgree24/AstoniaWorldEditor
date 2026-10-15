using System;
using System.Drawing;

namespace Astonia.Editor
{
    // Ring propagation follows server/src/los.cpp (Astonia engine, Daniel Brockhaus).
    // Item flags supply the temporary sight blocking that create.cpp adds at load time.
    public sealed class SightPreview
    {
        private readonly byte[,] distances = new byte[256, 256];
        public readonly Point Origin;
        public readonly int Range;
        private readonly MapDocument document;
        private readonly AssetCatalog catalog;
        public SightPreview(MapDocument document, AssetCatalog catalog, Point origin, int range)
        {
            this.document = document; this.catalog = catalog; Origin = origin; Range = Math.Max(2, Math.Min(64, range));
            Add(origin.X, origin.Y, 1);
            for (int yy = -1; yy <= 1; yy++) for (int xx = -1; xx <= 1; xx++) if (xx != 0 || yy != 0) Add(origin.X + xx, origin.Y + yy, 2);
            for (int distance = 2; distance < Range; distance++)
            {
                bool found = false;
                for (int x = origin.X - distance; x <= origin.X + distance; x++)
                {
                    if (Close(x, origin.Y - distance, 0, 1, distance)) { Add(x, origin.Y - distance, distance + 1); found = true; }
                    if (Close(x, origin.Y + distance, 0, -1, distance)) { Add(x, origin.Y + distance, distance + 1); found = true; }
                }
                for (int y = origin.Y - distance; y <= origin.Y + distance; y++)
                {
                    if (Close(origin.X - distance, y, 1, 0, distance)) { Add(origin.X - distance, y, distance + 1); found = true; }
                    if (Close(origin.X + distance, y, -1, 0, distance)) { Add(origin.X + distance, y, distance + 1); found = true; }
                }
                if (!found) break;
            }
        }
        private void Add(int x, int y, int distance) { if (document.Contains(x, y) && distances[x, y] == 0) distances[x, y] = (byte)distance; }
        private bool Passes(int x, int y) { return x >= 1 && y >= 1 && document.Contains(x, y) && !(catalog == null ? document.At(x, y).Flags.Contains("MF_SIGHTBLOCK") || document.At(x, y).Flags.Contains("MF_TSIGHTBLOCK") : catalog.IsSightBlocked(document.At(x, y))); }
        private bool Close(int x, int y, int dx, int dy, int distance)
        {
            for (int offset = -1; offset <= 1; offset++)
            {
                int nx = x + dx + (dx == 0 ? offset : 0), ny = y + dy + (dy == 0 ? offset : 0);
                if (document.Contains(nx, ny) && distances[nx, ny] == distance && Passes(nx, ny)) return true;
            }
            return false;
        }
        public bool Visible(int x, int y) { return document.Contains(x, y) && distances[x, y] > 0 && distances[x, y] < Range && Math.Abs(x - Origin.X) < Range && Math.Abs(y - Origin.Y) < Range; }
    }
}

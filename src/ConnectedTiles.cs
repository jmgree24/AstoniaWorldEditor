using System;
using System.Collections.Generic;
using System.Linq;

namespace Astonia.Editor
{
    public sealed class ConnectedTiles
    {
        public static readonly int[] DefaultWalls = { 21008, 21008, 21000, 21014, 21008, 21008, 21010, 21004, 21000, 21012, 21000, 21006, 21002, 21004, 21006, 21008 };
        public static readonly string[] Labels = { "Isolated", "End SE", "End SW", "Corner SE + SW", "End NW", "Straight SE / NW", "Corner SW + NW", "T: SE + SW + NW", "End NE", "Corner SE + NE", "Straight SW / NE", "T: SE + SW + NE", "Corner NW + NE", "T: SE + NW + NE", "T: SW + NW + NE", "Crossing" };
        private static readonly int[] dx = { 1, 0, -1, 0 }, dy = { 0, 1, 0, -1 };
        public int[] Pieces;
        public bool AlternateStraight;
        public int Layer = 2;
        private bool StoneFamily { get { return Pieces == null || Pieces.SequenceEqual(DefaultWalls); } }
        public bool Member(int id) { return StoneFamily ? id >= 21000 && id <= 21015 : Pieces.Contains(id); }
        public int Resolve(MapDocument map, int x, int y, int layer, int original)
        {
            if (!Member(original)) return original;
            int mask = 0;
            for (int bit = 0; bit < 4; bit++) if (map.Contains(x + dx[bit], y + dy[bit]) && Member(map.At(x + dx[bit], y + dy[bit]).Sprite(layer))) mask |= 1 << bit;
            if (!StoneFamily)
            {
                if (AlternateStraight && new[] { 0, 1, 2, 4, 5, 8, 10 }.Contains(mask)) { int[] variants = Pieces.Distinct().ToArray(); return variants[(x + y) % variants.Length]; }
                return Pieces[mask];
            }
            bool straight = mask == 1 || mask == 2 || mask == 4 || mask == 5 || mask == 8 || mask == 10;
            return mask == 0 ? original : DefaultWalls[mask] + (Pieces != null && AlternateStraight && straight ? (x + y) & 1 : original & 1);
        }
        public void Reconnect(MapDocument map, int x, int y, int layer)
        {
            for (int i = -1; i < 4; i++)
            {
                int xx = x + (i < 0 ? 0 : dx[i]), yy = y + (i < 0 ? 0 : dy[i]); if (!map.Contains(xx, yy)) continue;
                int old = map.At(xx, yy).Sprite(layer), next = Resolve(map, xx, yy, layer, old);
                if (old != next) map.Edit(xx, yy, tile => tile.SetSprite(layer, next));
            }
        }
    }
}

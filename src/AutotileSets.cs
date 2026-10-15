using System;
using System.Collections.Generic;
using System.Linq;

namespace Astonia.Editor
{
    public static class AutotileSets
    {
        public static List<LegacyPreset> Find(AssetCatalog assets, int sprite)
        {
            return assets.Presets.Where(p => p.IsAutotile && p.Error == "" && p.Sprites.Count > 1 && p.Sprites.Contains(sprite))
                .OrderByDescending(p => p.Sprites[0] == sprite)
                .ThenBy(p => p.Area == assets.ActiveArea ? 0 : p.Area == "generic" ? 1 : 2)
                .ThenBy(p => p.Source.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Ordinal)
                .GroupBy(p => p.Type + ":" + p.GridX + ":" + p.GridY + ":" + string.Join(",", p.Sprites))
                .Select(group => SpritePattern(group.First(), group.First().Affect)).ToList();
        }
        // Copy artwork and geometry without inheriting a preset's flags or layer clears.
        public static LegacyPreset SpritePattern(LegacyPreset source, int layer)
        {
            return new LegacyPreset { Type = source.Type, Name = source.Name, Source = source.Source, Area = source.Area,
                Ordinal = source.Ordinal, Affect = layer, GridX = source.GridX, GridY = source.GridY,
                Length = source.Length, Sprites = new List<int>(source.Sprites) };
        }
        public static LegacyPreset Sequential(int sprite, int columns, int rows, int layer, bool sequence)
        {
            int count = checked(columns * rows);
            if (columns < 1 || rows < 1 || count > 256 || sprite < 1 || (long)sprite + count - 1 > 65535)
                throw new ArgumentOutOfRangeException("sprite", "The set must contain 1–256 sprite IDs within 1–65535.");
            return new LegacyPreset { Type = sequence ? "LINEWALL" : "GROUND", Name = (sequence ? "Wall sequence " : "Ground pattern ") + sprite,
                Affect = layer, GridX = columns, GridY = rows, Length = count, Sprites = Enumerable.Range(sprite, count).ToList() };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Astonia.Editor
{
    public sealed class LegacyPreset
    {
        public string Type, Name = "", Area, Error = "";
        public SourceFile Source;
        public int Ordinal, Affect, GridX = 1, GridY = 1, Length;
        public List<int> Sprites = new List<int>(), Weights = new List<int>();
        public bool[] Enabled = new bool[6];
        public Tile Values = new Tile();
        public HashSet<string> SetFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase), ClearFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool IsAutotile { get { return Type == "GROUND" || Type == "LINEWALL"; } }
        public override string ToString() { return Name + "  ·  " + Type + "  [" + Area + "/" + System.IO.Path.GetFileName(Source.RelativePath) + "]" + (Error == "" ? "" : "  !"); }
        public int PreviewSprite { get { return Sprites.Count > 0 ? Sprites[0] : Enumerable.Range(0, 4).Select(i => Values.Sprite(i)).FirstOrDefault(i => i > 0); } }
        public static List<LegacyPreset> Parse(SourceFile source, string area)
        {
            var result = new List<LegacyPreset>(); LegacyPreset current = null;
            foreach (ZoneToken token in ZoneText.Parse(source.ReadText(), message => { source.Error = message; if (current != null) current.Error = message; }))
            {
                if (token.Header) { current = new LegacyPreset { Type = token.Key.ToUpperInvariant(), Source = source, Area = area, Ordinal = result.Count }; result.Add(current); continue; }
                if (token.End) { current = null; continue; } if (current == null) continue;
                try
                {
                    switch (token.Key)
                    {
                        case "name": current.Name = token.Value; break;
                        case "affect": current.Affect = ZoneText.Integer(token.Value); break;
                        case "gridx": current.GridX = ZoneText.Integer(token.Value); break;
                        case "gridy": current.GridY = ZoneText.Integer(token.Value); break;
                        case "len": current.Length = ZoneText.Integer(token.Value); break;
                        case "sprite": current.Sprites.Add(ZoneText.Integer(token.Value)); break;
                        case "prob": current.Weights.Add(ZoneText.Integer(token.Value)); break;
                        case "flag": case "setflag": current.SetFlags.Add(token.Value); break;
                        case "clrflag": current.ClearFlags.Add(token.Value); break;
                        case "gsprite": case "gsprite1": current.SetLayer(0, token.Value); break;
                        case "gsprite2": current.SetLayer(1, token.Value); break;
                        case "fsprite": current.SetLayer(2, token.Value); break;
                        case "fsprite2": current.SetLayer(3, token.Value); break;
                        case "it": current.Values.Item = token.Value; current.Enabled[4] = true; break;
                        case "ch": current.Values.Character = token.Value; current.Enabled[5] = true; break;
                        default: current.Error = "Unsupported preset directive: " + token.Key; break;
                    }
                }
                catch (Exception ex) { current.Error = ex.Message; }
            }
            foreach (LegacyPreset preset in result)
            {
                if (preset.Type != "PRESET" && preset.Type != "GROUND" && preset.Type != "LINEWALL" && preset.Type != "CHANCE") preset.Error = "Unknown type: " + preset.Type;
                if (preset.Affect < 0 || preset.Affect > 3) preset.Error = "affect must identify sprite layer 0..3.";
                if (preset.Sprites.Any(i => i < 0 || i > 65535)) preset.Error = "A map sprite ID is outside 0..65535.";
                if (preset.Type == "GROUND" && (preset.GridX < 1 || preset.GridY < 1 || (long)preset.GridX * preset.GridY != preset.Sprites.Count)) preset.Error = "Sprite count does not match the ground grid.";
                if ((preset.Type == "LINEWALL" || preset.Type == "CHANCE") && (preset.Length < 1 || preset.Length != preset.Sprites.Count)) preset.Error = "Sprite count does not match len.";
                if (preset.Weights.Count > preset.Sprites.Count || preset.Weights.Any(w => w < 0)) preset.Error = "Invalid chance weights.";
                if (preset.Type == "CHANCE" && Enumerable.Range(0, preset.Sprites.Count).Sum(i => (long)(i < preset.Weights.Count ? preset.Weights[i] : 100)) <= 0) preset.Error = "Chance weights total zero.";
            }
            return result;
        }
        private void SetLayer(int layer, string value) { Values.SetSprite(layer, ZoneText.Integer(value)); Enabled[layer] = true; }
        public string UnavailableReason(AssetCatalog catalog)
        {
            if (Error != "") return Error;
            if (Enabled[4] && Values.Item != "" && !catalog.Items.ContainsKey(Values.Item)) return "Item " + Values.Item + " is not defined for area " + catalog.ActiveArea + ".";
            if (Enabled[5] && Values.Character != "" && !catalog.Characters.ContainsKey(Values.Character)) return "NPC " + Values.Character + " is not defined for area " + catalog.ActiveArea + ".";
            return "";
        }
        public void Apply(MapDocument document, int x, int y, Tile tile, Random random)
        {
            if (Error != "") return;
            for (int layer = 0; layer < 4; layer++) if (Enabled[layer]) tile.SetSprite(layer, Type == "PRESET" ? Values.Sprite(layer) : 0);
            if (Enabled[4]) tile.Item = Type == "PRESET" ? Values.Item : "";
            if (Enabled[5]) tile.Character = Type == "PRESET" ? Values.Character : "";
            if (Type == "GROUND") tile.SetSprite(Affect, Sprites[(x % GridX) + (y % GridY) * GridX]);
            else if (Type == "LINEWALL")
            {
                int selected = 0; int[,] neighbors = { { -1, 0, 1 }, { 0, -1, -1 }, { 0, 1, 1 }, { 1, 0, -1 } };
                for (int n = 0; n < 4; n++)
                {
                    int nx = x + neighbors[n, 0], ny = y + neighbors[n, 1]; if (!document.Contains(nx, ny)) continue;
                    int pos = Sprites.IndexOf(document.At(nx, ny).Sprite(Affect)); if (pos < 0) continue;
                    selected = (pos + neighbors[n, 2] + Sprites.Count) % Sprites.Count; break;
                }
                tile.SetSprite(Affect, Sprites[selected]);
            }
            else if (Type == "CHANCE")
            {
                long total = Enumerable.Range(0, Sprites.Count).Sum(i => (long)(i < Weights.Count ? Weights[i] : 100));
                double roll = random.NextDouble() * total;
                for (int i = 0; i < Sprites.Count; i++) { roll -= i < Weights.Count ? Weights[i] : 100; if (roll < 0) { tile.SetSprite(Affect, Sprites[i]); break; } }
            }
            tile.Flags.ExceptWith(ClearFlags); tile.Flags.UnionWith(SetFlags);
        }
    }
}

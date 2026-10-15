using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Astonia.Editor
{
    public sealed class SourceFile
    {
        public string Root, RelativePath, FullPath, Status = "Indexed", Error = "", Text;
        public long Length;
        public int Scale = 1, Priority;
        public ZipArchiveEntry Entry;
        public object Json;
        public string Extension { get { return Path.GetExtension(Entry == null ? FullPath : Entry.Name).ToLowerInvariant(); } }
        private string displayPath;
        public string DisplayPath { get { return displayPath ?? (displayPath = Root + "/" + RelativePath); } }
        public Stream Open() { return Entry == null ? File.OpenRead(FullPath) : Entry.Open(); }
        public string ReadText() { if (Text == null) using (Stream s = Open()) using (var reader = new StreamReader(s)) Text = reader.ReadToEnd(); return Text; }
        public override string ToString() { return DisplayPath; }
    }
    public sealed class ZoneToken
    {
        public string Key, Value, RawKey;
        public bool Header, End;
    }
    public static class ZoneText
    {
        private static readonly Regex Token = new Regex(@"\G(?:\s+|\#[^\r\n]*|(?<end>;)|(?<header>[A-Za-z0-9_]+)\s*:|(?<key>[A-Za-z0-9_]+)\s*=\s*(?:""(?<quote>[^""]*)""|(?<bare>[^\s;#]+)))", RegexOptions.Compiled);
        public static IEnumerable<ZoneToken> Parse(string text, Action<string> recover = null)
        {
            int at = 0;
            while (at < text.Length)
            {
                Match m = Token.Match(text, at);
                if (!m.Success)
                {
                    string message = "Unrecognized zone text near: " + text.Substring(at, Math.Min(70, text.Length - at)).Replace('\n', ' ');
                    if (recover == null) throw new FormatException(message);
                    recover(message); int next = text.IndexOf('\n', at); at = next < 0 ? text.Length : next + 1; continue;
                }
                at += m.Length;
                if (m.Groups["header"].Success) yield return new ZoneToken { Header = true, Key = m.Groups["header"].Value };
                else if (m.Groups["end"].Success) yield return new ZoneToken { End = true };
                else if (m.Groups["key"].Success) yield return new ZoneToken { Key = m.Groups["key"].Value.ToLowerInvariant(), RawKey = m.Groups["key"].Value, Value = m.Groups["quote"].Success ? m.Groups["quote"].Value : m.Groups["bare"].Value };
            }
        }
        public static int Integer(string value)
        {
            int n; if (!int.TryParse(value, out n)) throw new FormatException("Expected integer, found " + value); return n;
        }
    }
    public sealed class FlagInfo
    {
        public string Name, Identifier;
        public Color Color;
        public override string ToString() { return Identifier + "  ·  " + Name; }
    }
    public static class EditorFlags
    {
        private static readonly string[] ids = { "M", "S", "TM", "TS", "I", "R", "D", "SO", "TSO", "SH", "C", "A", "P", "N", "F", "SD", "NL", "NM", "U", "NR" };
        public static readonly List<FlagInfo> All = MapDocument.FlagNames.Select((name, i) => new FlagInfo { Name = "MF_" + name, Identifier = ids[i], Color = ReferenceColor(name, i) }).ToList();
        private static Color ReferenceColor(string name, int index)
        {
            switch (name)
            {
                case "MOVEBLOCK": return ColorTranslator.FromHtml("#ed645d");
                case "SIGHTBLOCK": return ColorTranslator.FromHtml("#b786eb");
                case "INDOORS": return ColorTranslator.FromHtml("#58b8ed");
                case "PEACE": return ColorTranslator.FromHtml("#72d69b");
                case "NEUTRAL": return ColorTranslator.FromHtml("#e2d274");
                case "UNDERWATER": return ColorTranslator.FromHtml("#087cff");
            }
            double hue = (index * 137) % 360, chroma = (1 - Math.Abs(2 * 0.6 - 1)) * 0.65, second = chroma * (1 - Math.Abs(hue / 60 % 2 - 1)), offset = 0.6 - chroma / 2;
            double r = 0, g = 0, b = 0;
            if (hue < 60) { r = chroma; g = second; } else if (hue < 120) { r = second; g = chroma; } else if (hue < 180) { g = chroma; b = second; } else if (hue < 240) { g = second; b = chroma; } else if (hue < 300) { r = second; b = chroma; } else { r = chroma; b = second; }
            return Color.FromArgb((int)Math.Round((r + offset) * 255), (int)Math.Round((g + offset) * 255), (int)Math.Round((b + offset) * 255));
        }
        public static FlagInfo Find(string name) { return All.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? new FlagInfo { Name = name, Identifier = "?", Color = Color.Silver }; }
        public static string Identifiers(IEnumerable<string> names) { return string.Join(" ", names.Select(n => Find(n).Identifier)); }
    }
    public sealed class SpriteRule
    {
        public int Id, End, Stride, Result;
        public bool HasCut, Offset;
    }
    public sealed class CharacterArt
    {
        public int Frame;
        public float Scale = 1;
        public int PositionFrames;
        public SpriteEffects Effects;
    }
    public sealed class SpriteRules
    {
        private readonly Dictionary<int, SpriteRule> exact = new Dictionary<int, SpriteRule>();
        private readonly List<SpriteRule> ranges = new List<SpriteRule>();
        private readonly List<SpriteRule> heights = new List<SpriteRule>();
        private readonly Dictionary<int, CharacterArt> characters = new Dictionary<int, CharacterArt>();
        private readonly Dictionary<int, CharacterArt> animated = new Dictionary<int, CharacterArt>();
        public IEnumerable<int> VariantIds { get { return animated.Keys; } }
        public int CutCount { get { return exact.Values.Count(r => r.HasCut) + ranges.Count(r => r.HasCut); } }
        public static object ParseJson(string text) { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 }.DeserializeObject(text); }
        public static int Number(IDictionary<string, object> row, string key, int fallback = 0) { object value; return row.TryGetValue(key, out value) ? Convert.ToInt32(value) : fallback; }
        private static IEnumerable<IDictionary<string, object>> Rows(object json, string key)
        {
            var root = json as IDictionary<string, object>; object value;
            if (root == null || !root.TryGetValue(key, out value)) return Enumerable.Empty<IDictionary<string, object>>();
            return ((object[])value).OfType<IDictionary<string, object>>();
        }
        public void LoadMetadata(object json)
        {
            exact.Clear(); ranges.Clear(); heights.Clear();
            foreach (var row in Rows(json, "sprite_metadata"))
            {
                if (!row.ContainsKey("id")) continue;
                if (row.ContainsKey("yadd")) heights.Add(new SpriteRule { Id = Number(row, "id"), End = Number(row, "id_end", Number(row, "id")), Result = Number(row, "yadd") });
                var rule = new SpriteRule { Id = Number(row, "id"), End = Number(row, "id_end"), Stride = Number(row, "stride"), HasCut = row.ContainsKey("cut_offset") || row.ContainsKey("cut_sprite"), Offset = row.ContainsKey("cut_offset"), Result = row.ContainsKey("cut_offset") ? Number(row, "cut_offset") : Number(row, "cut_sprite") };
                if (rule.End > rule.Id) ranges.Add(rule); else exact[rule.Id] = rule;
            }
        }
        public int SurfaceHeight(int id) { SpriteRule height = heights.LastOrDefault(r => id >= r.Id && id <= r.End); return height == null ? 0 : height.Result; }
        public void LoadCharacters(object json)
        {
            characters.Clear();
            var rows = new Dictionary<int, IDictionary<string, object>>();
            foreach (var row in Rows(json, "character_variants")) if (row.ContainsKey("id")) rows[Number(row, "id")] = row;
            foreach (var row in rows.Values)
            {
                int id = Number(row, "id"), frame = Number(row, "base_sprite", id); IDictionary<string, object> parent;
                var merged = frame != id && rows.TryGetValue(frame, out parent) ? new Dictionary<string, object>(parent) : new Dictionary<string, object>();
                float parentScale = Number(merged, "scale", 100); foreach (var entry in row) merged[entry.Key] = entry.Value;
                characters[id] = new CharacterArt { Frame = frame, Scale = (int)(parentScale * Number(row, "scale", 100) / 100) / 100f, Effects = SpriteEffects.From(merged) };
            }
        }
        public void LoadAnimated(object json)
        {
            animated.Clear(); foreach (var row in Rows(json, "animated_variants")) if (row.ContainsKey("id") && row.ContainsKey("base_sprite"))
            {
                object animation; var settings = row.TryGetValue("animation", out animation) ? animation as IDictionary<string, object> : null;
                animated[Number(row, "id")] = new CharacterArt { Frame = Number(row, "base_sprite"), Scale = Number(row, "scale", 100) / 100f, Effects = SpriteEffects.From(row), PositionFrames = settings != null && settings.ContainsKey("type") && Convert.ToString(settings["type"]) == "position_cycle" ? Number(settings, "frames") : 0 };
            }
        }
        public int Lowered(int id)
        {
            SpriteRule rule;
            if (!exact.TryGetValue(id, out rule)) rule = ranges.Where(r => id >= r.Id && id <= r.End && (r.Stride <= 0 || (id - r.Id) % r.Stride == 0)).OrderBy(r => r.End - r.Id).FirstOrDefault();
            return rule == null || !rule.HasCut ? id : Math.Abs(rule.Offset ? id + rule.Result : rule.Result);
        }
        // Map layers and item templates share the client's sprite translation table.
        // Resolve exactly once: a base frame can itself be another logical variant.
        public int PreviewFrame(int id, int x = 0, int y = 0)
        {
            if (id == 0) return 0;
            CharacterArt variant; int frame = animated.TryGetValue(id, out variant) ? variant.Frame : id;
            if (variant != null && variant.PositionFrames > 0) frame += ((x + y * 256) % variant.PositionFrames + variant.PositionFrames) % variant.PositionFrames;
            if (frame >= 100000)
            {
                int number = (frame - 100000) / 1000;
                if (characters.TryGetValue(number, out variant)) frame = 100000 + variant.Frame * 1000 + frame % 1000;
            }
            return frame;
        }
        public SpriteEffects PreviewEffects(int id)
        {
            CharacterArt variant; animated.TryGetValue(id, out variant); SpriteEffects effects = variant == null ? null : variant.Effects; int frame = variant == null ? id : variant.Frame;
            if (frame >= 100000 && characters.TryGetValue((frame - 100000) / 1000, out variant)) effects = variant.Effects;
            return effects;
        }
        public float PreviewScale(int id)
        {
            CharacterArt variant; int frame = animated.TryGetValue(id, out variant) ? variant.Frame : id;
            float scale = variant == null ? 1 : variant.Scale;
            if (frame >= 100000 && characters.TryGetValue((frame - 100000) / 1000, out variant)) scale = variant.Scale;
            return scale;
        }
        public CharacterArt Npc(int number, int facing = 0)
        {
            CharacterArt variant; if (!characters.TryGetValue(number, out variant)) variant = new CharacterArt { Frame = number };
            int baseNumber = variant.Frame;
            bool idle = baseNumber == 129 || baseNumber == 128 || baseNumber == 45 || baseNumber == 120 || baseNumber == 121 || baseNumber == 122 || baseNumber == 360 || (baseNumber >= 63 && baseNumber <= 119 && (baseNumber % 5 == 3 || baseNumber % 5 == 4));
            return new CharacterArt { Frame = 100000 + baseNumber * 1000 + (idle ? 800 + facing * 8 : facing), Scale = variant.Scale, Effects = variant.Effects };
        }
    }
}

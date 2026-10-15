using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace Astonia.Editor
{
    public sealed class Template
    {
        public string Id, Name = "", Area;
        public int Sprite, Direction, Driver;
        public bool Character;
        public List<KeyValuePair<string, string>> Properties = new List<KeyValuePair<string, string>>();
        public string Argument { get { return Properties.Where(p => p.Key.Equals("arg", StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).FirstOrDefault() ?? ""; } }
        public bool IsDoor { get { return !Character && Driver != 0 && Flags.Contains("IF_USE") && (Flags.Contains("IF_DOOR") || Driver == 2 || Driver == 31 || (Flags.Contains("IF_MOVEBLOCK") && Regex.IsMatch(Id + " " + Name, "door|gate", RegexOptions.IgnoreCase))); } }
        public SourceFile Source;
        public HashSet<string> Flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public override string ToString() { return Id + (Name == "" ? "" : "  ·  " + Name) + "  [" + Area + "/" + Path.GetFileName(Source.RelativePath) + "]"; }
    }
    public sealed class SpriteArt
    {
        public Bitmap Image, Enhanced;
        public int FullWidth, FullHeight, CropX, CropY, Scale = 1;
    }
    public sealed class AssetCatalog : IDisposable
    {
        private readonly Dictionary<int, List<SourceFile>> spriteSources = new Dictionary<int, List<SourceFile>>();
        private readonly Dictionary<long, SourceFile> sourceCache = new Dictionary<long, SourceFile>();
        private readonly List<ZipArchive> archives = new List<ZipArchive>();
        private readonly Dictionary<string, SpriteArt> cache = new Dictionary<string, SpriteArt>();
        private readonly Queue<string> cacheOrder = new Queue<string>();
        private readonly HashSet<string> failedArt = new HashSet<string>();
        private long cachedPixels;
        private long enhancedPixels;
        public List<int> Ids = new List<int>();
        public List<SourceFile> Files = new List<SourceFile>(), MapFiles = new List<SourceFile>();
        public List<Template> AllItems = new List<Template>(), AllCharacters = new List<Template>();
        public List<LegacyPreset> Presets = new List<LegacyPreset>();
        public Dictionary<string, Template> Items = new Dictionary<string, Template>(StringComparer.OrdinalIgnoreCase), Characters = new Dictionary<string, Template>(StringComparer.OrdinalIgnoreCase);
        public SpriteRules Rules = new SpriteRules();
        public string Workspace, GraphicsDirectory, ZonesDirectory, ActiveArea = "1";
        public List<string> Warnings = new List<string>();
        private bool zonesLoaded;
        public int PhysicalResourceCount { get { return Files.Count(f => f.Root == "res" && f.Entry == null); } }
        public int ZoneFileCount { get { return Files.Count(f => f.Root == "zones"); } }
        public string Summary { get { return PhysicalResourceCount.ToString("N0") + " res files · " + ZoneFileCount + " zone files · " + Ids.Count.ToString("N0") + " sprites · " + Presets.Count + " presets · " + Warnings.Count + " warnings"; } }
        public void LoadGraphics(string directory)
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            ClearGraphics(); GraphicsDirectory = Path.GetFullPath(directory);
            foreach (string path in EnumerateFiles(GraphicsDirectory))
            {
                var source = Register("res", GraphicsDirectory, path);
                if (source.Extension == ".zip")
                {
                    try
                    {
                        ZipArchive zip = ZipFile.OpenRead(path); archives.Add(zip);
                        foreach (ZipArchiveEntry entry in zip.Entries)
                        {
                            if (entry.Name == "") continue;
                            var child = new SourceFile { Root = "res", FullPath = path, RelativePath = source.RelativePath + "!/" + entry.FullName, Entry = entry, Length = entry.Length, Scale = GraphicsScale(source.RelativePath), Priority = SourcePriority(source.RelativePath, false) };
                            Files.Add(child); RegisterSprite(child); LoadTextResource(child);
                        }
                        source.Status = "Loaded archive (" + zip.Entries.Count + " entries)";
                    }
                    catch (Exception ex) { Failure(source, ex); }
                }
                else { source.Scale = GraphicsScale(source.RelativePath); source.Priority = SourcePriority(source.RelativePath, true); RegisterSprite(source); LoadTextResource(source); }
            }
            foreach (var pair in spriteSources) pair.Value.Sort((a, b) => b.Priority != a.Priority ? b.Priority.CompareTo(a.Priority) : StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
            LoadRuleFile("sprite_metadata.json", json => Rules.LoadMetadata(json));
            LoadRuleFile("character_variants.json", json => Rules.LoadCharacters(json));
            LoadRuleFile("animated_variants.json", json => Rules.LoadAnimated(json));
            Ids = spriteSources.Keys.Concat(Rules.VariantIds).Distinct().OrderBy(i => i).ToList();
        }
        private void LoadRuleFile(string name, Action<object> load)
        {
            SourceFile source = Files.Where(f => f.Root == "res" && Path.GetFileName(f.RelativePath).Equals(name, StringComparison.OrdinalIgnoreCase) && f.Json != null).OrderBy(f => f.Entry == null && f.RelativePath.Equals("config/" + name, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(f => f.RelativePath).FirstOrDefault();
            if (source != null) try { load(source.Json); } catch (Exception ex) { Failure(source, ex); }
            else if (name == "sprite_metadata.json") Warnings.Add("config/sprite_metadata.json is unavailable; short-wall variants cannot be resolved.");
        }
        private static int GraphicsScale(string path)
        { Match m = Regex.Match(path.Replace('\\', '/'), @"(?:^|/)gx([1-4])(?:[_./]|$)", RegexOptions.IgnoreCase); return m.Success ? int.Parse(m.Groups[1].Value) : 1; }
        private static int SourcePriority(string path, bool loose)
        { return (path.IndexOf("_mod", StringComparison.OrdinalIgnoreCase) >= 0 ? 300 : path.IndexOf("_patch", StringComparison.OrdinalIgnoreCase) >= 0 ? 200 : 100) + (loose ? 1000 : 0); }
        private void RegisterSprite(SourceFile source)
        {
            string path = (source.Entry == null ? source.RelativePath : source.Entry.FullName).Replace('\\', '/'); int id;
            if (source.Extension != ".png" || path.Split('/').Any(p => p.Equals("masks", StringComparison.OrdinalIgnoreCase)) || !int.TryParse(Path.GetFileNameWithoutExtension(path), out id) || id < 0) return;
            List<SourceFile> list; if (!spriteSources.TryGetValue(id, out list)) spriteSources[id] = list = new List<SourceFile>(); list.Add(source); source.Status = "Sprite " + id + " · " + source.Scale + "x";
        }
        private void LoadTextResource(SourceFile source)
        {
            if (source.Extension != ".json" && source.Extension != ".ini" && source.Extension != ".md" && source.Extension != ".txt") return;
            try { string text = source.ReadText(); if (source.Extension == ".json") source.Json = SpriteRules.ParseJson(text); source.Status = "Loaded " + source.Extension.TrimStart('.').ToUpperInvariant(); }
            catch (Exception ex) { Failure(source, ex); }
        }
        public void LoadZones(string directory)
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            ZonesDirectory = Path.GetFullPath(directory); zonesLoaded = false;
            Warnings.RemoveAll(warning => warning.StartsWith("zones/", StringComparison.OrdinalIgnoreCase));
            Files.RemoveAll(f => f.Root == "zones"); AllItems.Clear(); AllCharacters.Clear(); Presets.Clear(); MapFiles.Clear();
            foreach (string path in EnumerateFiles(ZonesDirectory))
            {
                SourceFile source = Register("zones", ZonesDirectory, path); string area = source.RelativePath.Split('/')[0];
                if (!source.RelativePath.Contains('/')) area = "root";
                try
                {
                    if (source.Extension == ".itm" || source.Extension == ".chr")
                    {
                        List<Template> records = ReadTemplates(source, area); (source.Extension == ".chr" ? AllCharacters : AllItems).AddRange(records); source.Status = "Loaded " + records.Count + " templates";
                    }
                    else if (source.Extension == ".pre")
                    {
                        List<LegacyPreset> records = LegacyPreset.Parse(source, area); Presets.AddRange(records);
                        int errors = records.Count(p => p.Error != ""); source.Status = "Loaded " + records.Count + " presets" + (errors > 0 ? " (" + errors + " invalid)" : "");
                        foreach (LegacyPreset preset in records.Where(p => p.Error != "")) Warnings.Add(source.DisplayPath + " · " + preset.Name + ": " + preset.Error);
                    }
                    else if (source.Extension == ".map") { MapFiles.Add(source); source.Status = "Map ready to open"; }
                    else LoadTextResource(source);
                }
                catch (Exception ex) { Failure(source, ex); }
            }
            zonesLoaded = true; SetArea(ActiveArea);
        }
        public void LoadTemplates(string area) { if (!zonesLoaded && ZonesDirectory != null) LoadZones(ZonesDirectory); SetArea(area); }
        public void SetArea(string area)
        {
            ActiveArea = string.IsNullOrEmpty(area) ? "1" : area; Items.Clear(); Characters.Clear();
            foreach (Template record in AllItems.Concat(AllCharacters).Where(t => t.Area == "generic" || t.Area.Equals(ActiveArea, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Area == "generic" ? 0 : 1).ThenBy(t => t.Source.RelativePath, StringComparer.OrdinalIgnoreCase)) (record.Character ? Characters : Items)[record.Id] = record;
        }
        public string AreaForMap(string path)
        {
            string full = Path.GetFullPath(path);
            if (ZonesDirectory != null && full.StartsWith(ZonesDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return full.Substring(ZonesDirectory.Length).TrimStart('\\', '/').Split('\\', '/')[0];
            SourceFile match = MapFiles.FirstOrDefault(f => Path.GetFileName(f.FullPath).Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
            return match == null ? ActiveArea : match.RelativePath.Split('/')[0];
        }
        private List<Template> ReadTemplates(SourceFile source, string area)
        {
            var records = new List<Template>(); Template current = null;
            foreach (ZoneToken token in ZoneText.Parse(source.ReadText()))
            {
                if (token.Header) { current = new Template { Id = token.Key, Area = area, Source = source, Character = source.Extension == ".chr" }; records.Add(current); }
                else if (token.End) current = null;
                else if (current != null)
                {
                    current.Properties.Add(new KeyValuePair<string, string>(token.RawKey ?? token.Key, token.Value));
                    switch (token.Key)
                    {
                        case "sprite": current.Sprite = ZoneText.Integer(token.Value); break;
                        case "driver": current.Driver = ZoneText.Integer(token.Value); break;
                        case "name": current.Name = token.Value; break;
                        case "flag": current.Flags.Add(token.Value); break;
                        case "dir": current.Direction = Math.Max(0, Math.Min(7, ZoneText.Integer(token.Value) - 1)); break;
                    }
                }
            }
            return records;
        }
        private SourceFile Register(string root, string directory, string path)
        { var file = new SourceFile { Root = root, FullPath = path, RelativePath = path.Substring(directory.Length).TrimStart('\\', '/').Replace('\\', '/'), Length = new FileInfo(path).Length }; Files.Add(file); return file; }
        private void Failure(SourceFile source, Exception ex) { source.Error = ex.Message; source.Status = "Failed"; Warnings.Add(source.DisplayPath + ": " + ex.Message); }
        private IEnumerable<string> EnumerateFiles(string directory)
        {
            var stack = new Stack<string>(); stack.Push(directory);
            while (stack.Count > 0)
            {
                string folder = stack.Pop(); string[] files, children;
                try { files = Directory.GetFiles(folder); children = Directory.GetDirectories(folder); }
                catch (Exception ex) { Warnings.Add(folder + ": " + ex.Message); continue; }
                foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) yield return file;
                foreach (string child in children.OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) stack.Push(child); else Warnings.Add(child + ": linked folder skipped.");
            }
        }
        public bool HasSprite(int id) { return spriteSources.ContainsKey(id); }
        public SourceFile SpriteSource(int id, int preferredScale = 1)
        {
            preferredScale = Math.Max(1, Math.Min(4, preferredScale)); long key = (long)id * 8 + preferredScale; SourceFile cached; if (sourceCache.TryGetValue(key, out cached)) return cached;
            List<SourceFile> list; if (!spriteSources.TryGetValue(id, out list)) return null;
            return sourceCache[key] = list.OrderBy(f => f.Scale == preferredScale ? 0 : f.Scale < preferredScale ? 1 : 2).ThenBy(f => Math.Abs(f.Scale - preferredScale)).ThenByDescending(f => f.Priority).ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }
        public SpriteArt Art(int id, int preferredScale = 1, SpriteEffects effects = null)
        {
            if (id == 0) return null; SourceFile source = SpriteSource(id, Math.Max(1, Math.Min(4, preferredScale))); if (source == null) return null;
            string key = source.DisplayPath + (effects != null && effects.Active ? "|" + effects.Key : ""); SpriteArt art; if (cache.TryGetValue(key, out art)) return art; if (failedArt.Contains(key)) return null;
            try
            {
                using (Stream stream = source.Open()) using (var decoded = new Bitmap(stream)) using (var original = new Bitmap(decoded))
                {
                    original.MakeTransparent(Color.Magenta);
                    if (effects != null && effects.Active) effects.Apply(original);
                    int minX = original.Width, minY = original.Height, maxX = -1, maxY = -1;
                    var data = original.LockBits(new Rectangle(0, 0, original.Width, original.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        var bytes = new byte[Math.Abs(data.Stride) * data.Height]; System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                        for (int y = 0; y < original.Height; y++) for (int x = 0; x < original.Width; x++)
                            if (bytes[y * data.Stride + x * 4 + 3] > 0) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                    }
                    finally { original.UnlockBits(data); }
                    if (maxX < 0) { failedArt.Add(key); return null; }
                    art = new SpriteArt { Scale = source.Scale, FullWidth = original.Width, FullHeight = original.Height, CropX = minX, CropY = minY, Image = original.Clone(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1), PixelFormat.Format32bppArgb) };
                }
                long pixels = (long)art.Image.Width * art.Image.Height;
                while ((cache.Count >= 1536 || cachedPixels + pixels > 48 * 1024 * 1024) && cacheOrder.Count > 0) { string oldest = cacheOrder.Dequeue(); SpriteArt old = cache[oldest]; cachedPixels -= (long)old.Image.Width * old.Image.Height; DisposeArt(old); cache.Remove(oldest); }
                cache[key] = art; cacheOrder.Enqueue(key); cachedPixels += pixels; return art;
            }
            catch (Exception ex) { failedArt.Add(key); Failure(source, ex); return null; }
        }
        public Bitmap EnhancedImage(SpriteArt art)
        {
            if (art.Enhanced != null) return art.Enhanced;
            long pixels = (long)art.Image.Width * art.Image.Height * 16;
            if (enhancedPixels + pixels > 24 * 1024 * 1024) return art.Image;
            art.Enhanced = SpriteEnhancer.Enlarge(art.Image); enhancedPixels += pixels; return art.Enhanced;
        }
        private void DisposeArt(SpriteArt art) { art.Image.Dispose(); if (art.Enhanced != null) { enhancedPixels -= (long)art.Enhanced.Width * art.Enhanced.Height; art.Enhanced.Dispose(); } }
        public int LoweredSprite(int id) { int target = Rules.Lowered(id); return target == 0 || HasSprite(target) ? target : id; }
        public int PreviewSprite(int id, bool lowerWalls = false, int x = 0, int y = 0)
        {
            int frame = Rules.PreviewFrame(id, x, y);
            return lowerWalls ? LoweredSprite(frame) : frame;
        }
        public SpriteArt PreviewArt(int id, int preferredScale = 1, int x = 0, int y = 0, bool lowerWalls = false) { return Art(PreviewSprite(id, lowerWalls, x, y), preferredScale, Rules.PreviewEffects(id)); }
        public SpriteArt NpcArt(CharacterArt character, int preferredScale = 1) { return Art(character.Frame, preferredScale, character.Effects); }
        public int TemplateSprite(string id, bool character)
        {
            Template t; if (!(character ? Characters : Items).TryGetValue(id, out t)) return 0;
            if (!character) return Rules.PreviewFrame(t.Sprite);
            return Rules.Npc(t.Sprite, t.Direction).Frame;
        }
        public bool IsSightBlocked(Tile tile)
        {
            Template item; return tile.Flags.Contains("MF_SIGHTBLOCK") || tile.Flags.Contains("MF_TSIGHTBLOCK") || (Items.TryGetValue(tile.Item, out item) && item.Flags.Contains("IF_SIGHTBLOCK"));
        }
        public bool IsMoveBlocked(Tile tile)
        {
            Template item; return tile.Flags.Contains("MF_MOVEBLOCK") || tile.Flags.Contains("MF_TMOVEBLOCK") || (Items.TryGetValue(tile.Item, out item) && item.Flags.Contains("IF_MOVEBLOCK"));
        }
        public void PrepareDoorTile(Tile tile, string id)
        {
            Template door; if (!Items.TryGetValue(id, out door) || !door.IsDoor) return;
            for (int layer = 0; layer < 4; layer++) if (IsDoorArtwork(tile.Sprite(layer)) || tile.Sprite(layer) == door.Sprite || (door.Driver == 2 && tile.Sprite(layer) == door.Sprite + 1)) tile.SetSprite(layer, 0);
            if (door.Driver == 2) tile.Flags.ExceptWith(new[] { "MF_MOVEBLOCK", "MF_SIGHTBLOCK", "MF_SOUNDBLOCK", "MF_TMOVEBLOCK", "MF_TSIGHTBLOCK", "MF_TSOUNDBLOCK" });
        }
        public bool IsDoorArtwork(int id)
        {
            int frame = Rules.PreviewFrame(id);
            return Items.Values.Any(t => t.IsDoor && (Rules.PreviewFrame(t.Sprite) == frame || (t.Driver == 2 && Rules.PreviewFrame(t.Sprite + 1) == frame)));
        }
        private void ClearGraphics()
        {
            foreach (var art in cache.Values) DisposeArt(art); foreach (var archive in archives) archive.Dispose();
            archives.Clear(); cache.Clear(); cacheOrder.Clear(); spriteSources.Clear(); sourceCache.Clear(); failedArt.Clear(); cachedPixels = 0; Files.RemoveAll(f => f.Root == "res"); Rules = new SpriteRules();
        }
        public void Dispose() { ClearGraphics(); }
        public static string FindWorkspace(string start)
        {
            string dir = start;
            for (int i = 0; i < 5 && dir != null; i++, dir = Path.GetDirectoryName(dir))
                foreach (string path in new[] { dir, Path.Combine(dir, "Astonia_Windows_CPP_Port_Phase3") })
                    if (Directory.Exists(Path.Combine(path, "server", "zones")) && Directory.Exists(Path.Combine(path, "client", "res"))) return path;
            return null;
        }
    }
}

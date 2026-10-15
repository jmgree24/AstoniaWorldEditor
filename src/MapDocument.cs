using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Astonia.Editor
{
    public sealed class Tile
    {
        public uint Ground, Foreground;
        public string Item = "", Character = "";
        public HashSet<string> Flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Extra = new List<string>();
        public Tile Clone()
        {
            return new Tile { Ground = Ground, Foreground = Foreground, Item = Item, Character = Character,
                Flags = new HashSet<string>(Flags, StringComparer.OrdinalIgnoreCase), Extra = new List<string>(Extra) };
        }
        public int Sprite(int layer)
        {
            uint value = layer < 2 ? Ground : Foreground;
            return (int)((value >> ((layer % 2) * 16)) & 65535);
        }
        public void SetSprite(int layer, int id)
        {
            if (layer < 0 || layer > 3 || id < 0 || id > 65535) throw new ArgumentOutOfRangeException("id");
            int shift = (layer % 2) * 16;
            uint mask = 65535u << shift;
            if (layer < 2) Ground = (Ground & ~mask) | ((uint)id << shift);
            else Foreground = (Foreground & ~mask) | ((uint)id << shift);
        }
        public bool SameAs(Tile other)
        {
            return Ground == other.Ground && Foreground == other.Foreground && Item == other.Item &&
                Character == other.Character && Flags.SetEquals(other.Flags) && Extra.SequenceEqual(other.Extra);
        }
        public bool HasData { get { return Ground != 0 || Foreground != 0 || Item != "" || Character != "" || Flags.Count != 0 || Extra.Count != 0; } }
    }

    public sealed class MapDocument
    {
        public const int Size = 256;
        public static readonly string[] FlagNames = { "MOVEBLOCK", "SIGHTBLOCK", "TMOVEBLOCK", "TSIGHTBLOCK", "INDOORS", "RESTAREA", "DOOR", "SOUNDBLOCK", "TSOUNDBLOCK", "SHOUTBLOCK", "CLAN", "ARENA", "PEACE", "NEUTRAL", "FIRETHRU", "SLOWDEATH", "NOLIGHT", "NOMAGIC", "UNDERWATER", "NOREGEN" };
        public Tile[] Tiles = Enumerable.Range(0, Size * Size).Select(i => new Tile()).ToArray();
        public List<string> Header = new List<string>();
        public string SourcePath;
        public string SavePath;
        public string AreaContext;
        private const string AreaMarker = "# AstoniaWorldEditor.Area=";
        public string Name = "Untitled.map";
        public bool Dirty { get; private set; }
        public int Revision { get; private set; }
        private int savedRevision, nextRevision;
        private Dictionary<int, Tile> pending;
        private readonly Stack<ChangeSet> undo = new Stack<ChangeSet>(), redo = new Stack<ChangeSet>();
        private static readonly Regex Directive = new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:""([^""]*)""|([^#\s]+))\s*(?:#.*)?$", RegexOptions.Compiled);
        private sealed class ChangeSet
        {
            public Dictionary<int, Tile> Before, After;
            public int BeforeRevision, AfterRevision;
        }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public bool Contains(int x, int y) { return x >= 0 && y >= 0 && x < Size && y < Size; }
        public Tile At(int x, int y) { return Tiles[x + y * Size]; }
        public static MapDocument Load(string path) { var map = Parse(File.ReadAllText(path)); map.SourcePath = Path.GetFullPath(path); map.Name = Path.GetFileName(path); return map; }
        public static MapDocument Parse(string text)
        {
            var map = new MapDocument();
            int ox = 0, oy = 0, x = 0, y = 0, fx = 0, fy = 0, lineNo = 0;
            Tile current = null;
            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                lineNo++;
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#"))
                {
                    if (current == null && line.StartsWith(AreaMarker, StringComparison.Ordinal))
                    { string area = line.Substring(AreaMarker.Length).Trim(); if (Regex.IsMatch(area, "^[A-Za-z0-9_]+$")) map.AreaContext = area; }
                    (current == null ? map.Header : current.Extra).Add(raw); continue;
                }
                Match m = Directive.Match(line);
                if (!m.Success) throw new FormatException("Line " + lineNo + ": invalid map directive: " + line);
                string key = m.Groups[1].Value.ToLowerInvariant(), value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                if (key == "origin") { Pair(value, out ox, out oy); continue; }
                if (key == "field")
                {
                    Pair(value, out x, out y); x += ox; y += oy;
                    if (!map.Contains(x, y)) throw new FormatException("Line " + lineNo + ": tile is outside the 256 x 256 server map.");
                    current = new Tile(); map.Tiles[x + y * Size] = current; continue;
                }
                if (key == "from") { Pair(value, out fx, out fy); fx += ox; fy += oy; continue; }
                if (key == "to")
                {
                    int tx, ty; Pair(value, out tx, out ty); tx += ox; ty += oy;
                    if (current == null || !map.Contains(fx, fy) || !map.Contains(tx, ty) || fx > tx || fy > ty) throw new FormatException("Line " + lineNo + ": invalid map range.");
                    Tile source = current.Clone();
                    for (int yy = fy; yy <= ty; yy++) for (int xx = fx; xx <= tx; xx++) map.Tiles[xx + yy * Size] = source.Clone();
                    current = map.At(x, y); continue;
                }
                if (current == null) throw new FormatException("Line " + lineNo + ": expected field before " + key + ".");
                switch (key)
                {
                    case "gsprite": current.Ground = Packed(value); break;
                    case "fsprite": current.Foreground = Packed(value); break;
                    case "it": current.Item = value; break;
                    case "ch": current.Character = value; break;
                    case "flag": current.Flags.Add(value); break;
                    default: current.Extra.Add(raw); break;
                }
                // Keep comments on known directives without duplicating their values.
                int comment = CommentStart(raw);
                if (comment >= 0 && key != "flag" && key != "it" && key != "ch" && key != "gsprite" && key != "fsprite") continue;
                if (comment >= 0) current.Extra.Add(raw.Substring(comment));
            }
            return map;
        }
        private static int CommentStart(string line)
        {
            bool quoted = false;
            for (int i = 0; i < line.Length; i++) { if (line[i] == '"') quoted = !quoted; if (line[i] == '#' && !quoted) return i; }
            return -1;
        }
        private static uint Packed(string value)
        {
            long n; if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < int.MinValue || n > uint.MaxValue) throw new FormatException("Invalid packed sprite: " + value);
            return unchecked((uint)n);
        }
        private static void Pair(string value, out int x, out int y)
        {
            string[] parts = value.Split(',');
            x = y = 0;
            if (parts.Length != 2 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)) throw new FormatException("Expected x,y coordinates: " + value);
        }
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (string line in Header) if (!line.TrimStart().StartsWith(AreaMarker, StringComparison.Ordinal)) sb.AppendLine(line);
            if (Header.Count == 0) sb.AppendLine("# Astonia World Editor");
            if (AreaContext != null && Regex.IsMatch(AreaContext, "^[A-Za-z0-9_]+$")) sb.AppendLine(AreaMarker + AreaContext);
            sb.AppendLine("origin=\"0,0\"");
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                Tile tile = At(x, y);
                // Emit every field: explicit empty fields also replace earlier server data.
                sb.Append("\r\nfield=\"").Append(x).Append(',').Append(y).AppendLine("\"");
                if (tile.Ground != 0) sb.Append("gsprite=").AppendLine(unchecked((int)tile.Ground).ToString(CultureInfo.InvariantCulture));
                if (tile.Foreground != 0) sb.Append("fsprite=").AppendLine(unchecked((int)tile.Foreground).ToString(CultureInfo.InvariantCulture));
                if (tile.Item != "") sb.Append("it=\"").Append(tile.Item).AppendLine("\"");
                if (tile.Character != "") sb.Append("ch=\"").Append(tile.Character).AppendLine("\"");
                foreach (string flag in tile.Flags.OrderBy(f => f, StringComparer.Ordinal)) sb.Append("flag=").AppendLine(flag);
                foreach (string line in tile.Extra) sb.AppendLine(line);
            }
            return sb.ToString();
        }
        public void Save(string path)
        {
            string full = Path.GetFullPath(path), directory = Path.GetDirectoryName(full);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, Serialize(), new UTF8Encoding(false));
                // Parse the complete output before replacing any file.
                MapDocument verification = Load(temporary);
                for (int i = 0; i < Tiles.Length; i++) if (!Tiles[i].SameAs(verification.Tiles[i])) throw new IOException("Save verification failed at tile " + (i % Size) + "," + (i / Size));
                if (File.Exists(full))
                {
                    string backupDir = Path.Combine(directory, ".editor-backups"); Directory.CreateDirectory(backupDir);
                    string backup = Path.Combine(backupDir, Path.GetFileName(full) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".bak");
                    File.Replace(temporary, full, backup);
                }
                else File.Move(temporary, full);
                SavePath = full; Name = Path.GetFileName(full); savedRevision = Revision; Dirty = false;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void BeginEdit() { if (pending == null) pending = new Dictionary<int, Tile>(); }
        public void AcceptSavedPath(string path) { SavePath = Path.GetFullPath(path); Name = Path.GetFileName(path); savedRevision = Revision; Dirty = false; }
        public void Edit(int x, int y, Action<Tile> action)
        {
            if (!Contains(x, y)) return;
            if (pending == null) BeginEdit();
            int index = x + y * Size;
            if (!pending.ContainsKey(index)) pending[index] = Tiles[index].Clone();
            action(Tiles[index]);
        }
        public bool EndEdit()
        {
            if (pending == null) return false;
            var before = pending.Where(p => !p.Value.SameAs(Tiles[p.Key])).ToDictionary(p => p.Key, p => p.Value);
            pending = null; if (before.Count == 0) return false;
            undo.Push(new ChangeSet { Before = before, After = before.Keys.ToDictionary(i => i, i => Tiles[i].Clone()), BeforeRevision = Revision, AfterRevision = ++nextRevision });
            redo.Clear(); Revision = nextRevision; Dirty = Revision != savedRevision; return true;
        }
        public void Undo() { EndEdit(); if (!CanUndo) return; ChangeSet c = undo.Pop(); foreach (var p in c.Before) Tiles[p.Key] = p.Value.Clone(); Revision = c.BeforeRevision; redo.Push(c); Dirty = Revision != savedRevision; }
        public void Redo() { EndEdit(); if (!CanRedo) return; ChangeSet c = redo.Pop(); foreach (var p in c.After) Tiles[p.Key] = p.Value.Clone(); Revision = c.AfterRevision; undo.Push(c); Dirty = Revision != savedRevision; }
        public int Flood(int x, int y, Func<Tile, string> key, Action<Tile> paint)
        {
            if (!Contains(x, y)) return 0;
            string match = key(At(x, y)); var seen = new bool[Size * Size]; var queue = new Queue<int>(); queue.Enqueue(x + y * Size); int count = 0;
            BeginEdit();
            while (queue.Count > 0)
            {
                int n = queue.Dequeue(); if (seen[n]) continue; seen[n] = true;
                if (key(Tiles[n]) != match) continue;
                Edit(n % Size, n / Size, paint); count++;
                if (n % Size > 0) queue.Enqueue(n - 1); if (n % Size < Size - 1) queue.Enqueue(n + 1);
                if (n >= Size) queue.Enqueue(n - Size); if (n < Size * (Size - 1)) queue.Enqueue(n + Size);
            }
            EndEdit(); return count;
        }
    }
}

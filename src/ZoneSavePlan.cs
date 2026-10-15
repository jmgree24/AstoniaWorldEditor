using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Astonia.Editor
{
    public sealed class ZoneFileChange
    {
        public string Path, Before, After;
    }
    public sealed class ZoneSavePlan
    {
        public readonly List<ZoneFileChange> Files = new List<ZoneFileChange>();
        public readonly List<string> Summary = new List<string>();
        public static ZoneSavePlan Create(AssetCatalog assets, MapDocument baseline, MapDocument edited)
        {
            if (baseline == null || baseline.SourcePath == null || !File.Exists(baseline.SourcePath)) throw new IOException("Open a source zone map before saving linked zone files.");
            string area = assets.AreaForMap(baseline.SourcePath); int areaNumber;
            if (!int.TryParse(area, out areaNumber)) throw new IOException("Linked return updates require a numbered zone.");
            var plan = new ZoneSavePlan(); string originalMap = File.ReadAllText(baseline.SourcePath);
            MapDocument onDisk = MapDocument.Parse(originalMap);
            if (!onDisk.Header.SequenceEqual(baseline.Header) || !onDisk.Tiles.Zip(baseline.Tiles, (a, b) => a.SameAs(b)).All(same => same)) throw new IOException("The source map changed outside the editor. Reopen it before saving zone files.");
            plan.Files.Add(new ZoneFileChange { Path = baseline.SourcePath, Before = originalMap, After = edited.Serialize() });
            plan.Summary.Add("Map: " + baseline.SourcePath);
            var originals = Positions(baseline); var destinations = Positions(edited);
            var replacements = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in originals)
            {
                List<Point> moved; if (!destinations.TryGetValue(entry.Key, out moved)) continue;
                if (entry.Value.Count != 1 || moved.Count != 1)
                {
                    Template repeated;
                    if (entry.Value.Except(moved).Any() && assets.Items.TryGetValue(entry.Key, out repeated) && repeated.Driver == 10) plan.Summary.Add(entry.Key + ": repeated transition placements; return arguments need manual review.");
                    continue;
                }
                if (moved[0] == entry.Value[0]) continue;
                Template transition; int tx, ty, ta;
                if (!assets.Items.TryGetValue(entry.Key, out transition) || !TemplateStore.TransitionDestination(transition, out tx, out ty, out ta)) continue;
                Point old = entry.Value[0], next = moved[0]; int targetArea = ta == 0 ? areaNumber : ta;
                foreach (Template candidate in assets.AllItems.Where(t => t.Area == targetArea.ToString()))
                {
                    int rx, ry, ra; if (!TemplateStore.TransitionDestination(candidate, out rx, out ry, out ra) || rx != old.X || ry != old.Y || (ra == 0 ? targetArea : ra) != areaNumber) continue;
                    Dictionary<string, string> byId; if (!replacements.TryGetValue(candidate.Source.FullPath, out byId)) replacements[candidate.Source.FullPath] = byId = new Dictionary<string, string>();
                    string replacement = TemplateStore.TransitionArgument(next.X, next.Y, ra, candidate.Argument.Substring(12));
                    if (byId.ContainsKey(candidate.Id) && byId[candidate.Id] != replacement) throw new IOException("Ambiguous return arguments for " + candidate.Id);
                    byId[candidate.Id] = replacement;
                    plan.Summary.Add(candidate.Source.DisplayPath + " · " + candidate.Id + ": return " + old.X + "," + old.Y + " → " + next.X + "," + next.Y);
                }
            }
            foreach (var file in replacements)
            {
                string before = File.ReadAllText(file.Key), after = before;
                SourceFile loaded = assets.Files.First(f => f.FullPath == file.Key && f.Root == "zones");
                if (loaded.ReadText() != before) throw new IOException("A linked template file changed outside the editor. Reload sources: " + file.Key);
                foreach (var replacement in file.Value)
                {
                    string pattern = @"(?ms)(^\s*" + Regex.Escape(replacement.Key) + @"\s*:\s*\r?\n)(.*?)(^\s*;)";
                    bool found = false;
                    after = Regex.Replace(after, pattern, match =>
                    {
                        string body = Regex.Replace(match.Groups[2].Value, @"(?im)^(\s*arg\s*=\s*)(?:""[^""]*""|[^\s#;]+)", arg => { found = true; return arg.Groups[1].Value + "\"" + replacement.Value + "\""; });
                        return match.Groups[1].Value + body + match.Groups[3].Value;
                    });
                    if (!found) throw new IOException("Could not locate return argument for " + replacement.Key);
                }
                ZoneText.Parse(after).ToList(); plan.Files.Add(new ZoneFileChange { Path = file.Key, Before = before, After = after });
            }
            if (replacements.Count == 0) plan.Summary.Add("No linked return arguments need updating.");
            return plan;
        }
        private static Dictionary<string, List<Point>> Positions(MapDocument map)
        {
            var result = new Dictionary<string, List<Point>>(StringComparer.OrdinalIgnoreCase);
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            { string item = map.At(x, y).Item; if (item == "") continue; List<Point> list; if (!result.TryGetValue(item, out list)) result[item] = list = new List<Point>(); list.Add(new Point(x, y)); }
            return result;
        }
        public void Commit()
        {
            foreach (ZoneFileChange file in Files) if (File.ReadAllText(file.Path) != file.Before) throw new IOException("A source file changed; create a new save preview: " + file.Path);
            var saved = new List<ZoneFileChange>();
            try { foreach (ZoneFileChange file in Files) { TemplateStore.WriteVerified(file.Path, file.After, file.Before); saved.Add(file); } }
            catch
            {
                foreach (ZoneFileChange file in saved.AsEnumerable().Reverse()) TemplateStore.WriteVerified(file.Path, file.Before, file.After);
                throw;
            }
        }
    }
}

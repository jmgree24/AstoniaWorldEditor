using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Astonia.Editor
{
    public static class ArtCategories
    {
        private static readonly Dictionary<string, HashSet<int>> groups = Load();
        private static Dictionary<string, HashSet<int>> Load()
        {
            var result = new Dictionary<string, HashSet<int>>();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Astonia.Editor.ArtCategories.json"))
                if (stream != null) using (var reader = new StreamReader(stream))
                {
                    var json = SpriteRules.ParseJson(reader.ReadToEnd()) as IDictionary<string, object>;
                    foreach (var entry in json) result[entry.Key] = new HashSet<int>(((object[])entry.Value).Select(Convert.ToInt32));
                }
            return result;
        }
        public static bool Contains(string category, int id) { HashSet<int> ids; return groups.TryGetValue(category, out ids) && ids.Contains(id); }
        public static IEnumerable<int> Ids(AssetCatalog catalog, string category)
        { return catalog.Ids.Where(id => Contains(category, id) || Contains(category, catalog.Rules.PreviewFrame(id))); }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Astonia.Editor
{
    public sealed class EditorPreferences
    {
        public string[] VisibleFlags = { "MF_MOVEBLOCK", "MF_SIGHTBLOCK", "MF_INDOORS" };
        public int[] WallPieces = (int[])ConnectedTiles.DefaultWalls.Clone(), GroundPieces = Enumerable.Repeat(12000, 16).ToArray();
        public bool AlternateStraight = true;
        public static EditorPreferences Load(string path)
        {
            try
            {
                var value = new JavaScriptSerializer().Deserialize<EditorPreferences>(File.ReadAllText(path));
                if (value == null || value.VisibleFlags == null || value.WallPieces == null || value.GroundPieces == null || value.WallPieces.Length != 16 || value.GroundPieces.Length != 16 || value.WallPieces.Concat(value.GroundPieces).Any(id => id < 1 || id > 65535)) return new EditorPreferences();
                value.VisibleFlags = value.VisibleFlags.Where(flag => flag != "MF_NEUTRAL" && EditorFlags.All.Any(f => f.Name == flag)).ToArray(); return value;
            }
            catch { return new EditorPreferences(); }
        }
        public void Save(string path) { File.WriteAllText(path, new JavaScriptSerializer().Serialize(this)); }
    }
}

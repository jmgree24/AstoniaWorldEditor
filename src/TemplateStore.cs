using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Astonia.Editor
{
    public static class TemplateStore
    {
        public static string Destination(string zones, string area, string file, bool character)
        {
            if (!Regex.IsMatch(area, "^[A-Za-z0-9_]+$") || Path.GetFileName(file) != file || !Regex.IsMatch(file, @"^[A-Za-z0-9_. -]+\.(itm|chr)$", RegexOptions.IgnoreCase) || !file.EndsWith(character ? ".chr" : ".itm", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Choose an area and an .itm / .chr filename without folders.");
            string root = Path.GetFullPath(zones).TrimEnd('\\') + "\\", target = Path.GetFullPath(Path.Combine(root, area, file));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(Path.GetDirectoryName(target))) throw new IOException("Choose an existing zone folder.");
            return target;
        }
        public static string Definition(string id, IEnumerable<KeyValuePair<string, string>> properties)
        {
            if (!Regex.IsMatch(id, "^[A-Za-z_][A-Za-z0-9_]{0,38}$")) throw new FormatException("Template IDs must start with a letter or underscore and contain at most 39 letters, digits or underscores.");
            var text = new StringBuilder(id + ":\r\n"); bool sprite = false;
            foreach (var property in properties)
            {
                if (!Regex.IsMatch(property.Key, "^[A-Za-z_][A-Za-z0-9_]*$") || property.Value.IndexOfAny(new[] { '\r', '\n', '"', '\0' }) >= 0) throw new FormatException("Property names must be identifiers; values cannot contain quotes or newlines.");
                if (property.Key.Equals("sprite", StringComparison.OrdinalIgnoreCase)) { int number; if (!int.TryParse(property.Value, out number) || number < 0) throw new FormatException("Sprite must be a nonnegative integer."); sprite = true; }
                text.Append(property.Key).Append("=\"").Append(property.Value).Append("\"\r\n");
            }
            if (!sprite) throw new FormatException("Add a sprite property.");
            text.Append(";\r\n"); ZoneText.Parse(text.ToString()).ToList(); return text.ToString();
        }
        public static string Save(AssetCatalog catalog, string area, string file, bool character, string id, IEnumerable<KeyValuePair<string, string>> properties, string expectedText)
        {
            string path = Destination(catalog.ZonesDirectory, area, file, character);
            if (catalog.AllItems.Concat(catalog.AllCharacters).Any(t => t.Area == area && t.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) throw new IOException("That template ID already exists in this area.");
            string current = File.Exists(path) ? File.ReadAllText(path) : null;
            if (current != expectedText) throw new IOException("The template file changed since it was opened. Reopen the form before saving.");
            string next = (current ?? "").TrimEnd() + "\r\n\r\n" + Definition(id, properties);
            ZoneText.Parse(next).ToList(); WriteVerified(path, next, current); return path;
        }
        public static void WriteVerified(string path, string next, string expected)
        {
            if ((File.Exists(path) ? File.ReadAllText(path) : null) != expected) throw new IOException("A source file changed before saving: " + path);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, next, new UTF8Encoding(false));
                if (File.ReadAllText(temp) != next) throw new IOException("Could not verify the staged file.");
                if (File.Exists(path))
                {
                    string backups = Path.Combine(Path.GetDirectoryName(path), ".editor-backups"); Directory.CreateDirectory(backups);
                    File.Replace(temp, path, Path.Combine(backups, Path.GetFileName(path) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".bak"));
                }
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static string TransitionArgument(int x, int y, int area, string tail = "01")
        {
            if (x < 1 || x > 254 || y < 1 || y > 254 || area < 0 || area > 65535) throw new FormatException("Destination coordinates must be 1–254; area must be 0–65535.");
            Func<int, string> word = value => (value & 255).ToString("X2") + (value >> 8).ToString("X2");
            return word(x) + word(y) + word(area) + tail;
        }
        public static bool TransitionDestination(Template template, out int x, out int y, out int area)
        {
            x = y = area = 0; string argument = template.Argument;
            if (template.Driver != 10 || !Regex.IsMatch(argument, "^[0-9a-fA-F]{12,}$") || argument.Length % 2 != 0) return false;
            Func<int, int> word = offset => Convert.ToInt32(argument.Substring(offset + 2, 2) + argument.Substring(offset, 2), 16);
            x = word(0); y = word(4); area = word(8); return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Astonia.Editor
{
    public sealed class TemplateForm : Form
    {
        public string CreatedId, CreatedArea;
        public bool CreatedCharacter;
        private readonly AssetCatalog assets;
        private readonly ComboBox type = Theme.Combo(130), area = Theme.Combo(120), copy = Theme.Combo(270);
        private readonly TextBox file = Theme.TextBox(190), id = Theme.TextBox(220);
        private readonly DataGridView properties = new DataGridView();
        private readonly PictureBox preview = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Width = 100, Height = 90, BackColor = Theme.Control };
        private readonly NumericUpDown x = Number(254, 128), y = Number(254, 128), destinationArea = Number(65535, 0);
        private readonly Panel destination = new Panel { Height = 63, Dock = DockStyle.Top };
        private readonly Label message = Theme.Label("", 700, 45);
        private string expectedPath, expectedText, transitionTail = "01";
        private bool loading;
        private readonly int initialSprite;
        private bool Character { get { return type.SelectedIndex == 1; } }
        private bool Transition { get { return type.SelectedIndex >= 2; } }
        private static NumericUpDown Number(int maximum, int value) { return new NumericUpDown { Width = 94, Maximum = maximum, Minimum = maximum == 254 ? 1 : 0, Value = value, BackColor = Theme.Control, ForeColor = Theme.Text }; }
        public TemplateForm(AssetCatalog catalog, int brushSprite = 12000)
        {
            assets = catalog; initialSprite = brushSprite; Text = "New item / NPC template"; ClientSize = new Size(760, 710); MinimumSize = new Size(700, 620); StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Panel; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9);
            type.Items.AddRange(new[] { "Item", "NPC", "Hole", "Ladder" }); type.SelectedIndex = 0;
            area.Items.AddRange(Directory.GetDirectories(assets.ZonesDirectory).Select(Path.GetFileName).Where(name => System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]+$")).OrderBy(name => name).ToArray()); area.SelectedItem = assets.ActiveArea; if (area.SelectedIndex < 0 && area.Items.Count > 0) area.SelectedIndex = 0;
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 185, Padding = new Padding(12), ColumnCount = 4, RowCount = 4 };
            header.Controls.Add(Theme.Label("Type", 135), 0, 0); header.Controls.Add(type, 0, 1); header.Controls.Add(Theme.Label("Zone folder", 130), 1, 0); header.Controls.Add(area, 1, 1); header.Controls.Add(Theme.Label("File", 210), 2, 0); header.Controls.Add(file, 2, 1);
            header.Controls.Add(Theme.Label("Unique template ID", 240), 0, 2); header.SetColumnSpan(header.GetControlFromPosition(0, 2), 2); header.Controls.Add(id, 0, 3); header.SetColumnSpan(id, 2);
            header.Controls.Add(Theme.Label("Start from", 270), 2, 2); header.Controls.Add(copy, 2, 3); header.Controls.Add(preview, 3, 0); header.SetRowSpan(preview, 4);
            var transitionFields = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) }; transitionFields.Controls.Add(Theme.Label("Destination area", 112)); transitionFields.Controls.Add(destinationArea); transitionFields.Controls.Add(Theme.Label("Landing X", 72)); transitionFields.Controls.Add(x); transitionFields.Controls.Add(Theme.Label("Landing Y", 72)); transitionFields.Controls.Add(y); destination.Controls.Add(transitionFields);
            properties.Dock = DockStyle.Fill; properties.BackgroundColor = Theme.Control; properties.BorderStyle = BorderStyle.None; properties.AllowUserToAddRows = true; properties.AllowUserToDeleteRows = true; properties.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; properties.RowHeadersWidth = 30; properties.EnableHeadersVisualStyles = false;
            properties.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel; properties.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text; properties.DefaultCellStyle.BackColor = Theme.Control; properties.DefaultCellStyle.ForeColor = Theme.Text; properties.DefaultCellStyle.SelectionBackColor = Theme.Active; properties.DefaultCellStyle.SelectionForeColor = Theme.Text; properties.Columns.Add("property", "Property"); properties.Columns.Add("value", "Value (repeated properties supported)"); properties.Columns[0].FillWeight = 32;
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 108, Padding = new Padding(12) }; message.ForeColor = Theme.Gold; bottom.Controls.Add(message);
            var save = Theme.Button("Save template & select brush", 240); bottom.Controls.Add(save);
            var remove = Theme.Button("Remove selected rows", 200); remove.Click += (s, e) => { foreach (DataGridViewRow row in properties.SelectedRows) if (!row.IsNewRow) properties.Rows.Remove(row); }; bottom.Controls.Add(remove);
            Controls.Add(properties); Controls.Add(destination); Controls.Add(header); Controls.Add(bottom);
            type.SelectedIndexChanged += (s, e) => RefreshChoices(); area.SelectedIndexChanged += (s, e) => RefreshChoices();
            copy.SelectedIndexChanged += (s, e) =>
            {
                if (loading) return; Template template = copy.SelectedItem as Template;
                if (template == null) { Defaults(); return; }
                if (Transition && template.Driver != 10) { message.Text = "Choose a standard driver-10 hole/ladder, or choose Item to clone custom behavior."; return; }
                var rows = template.Properties.ToList(); int xx, yy, aa;
                if (Transition && TemplateStore.TransitionDestination(template, out xx, out yy, out aa)) { x.Value = Math.Max(1, Math.Min(254, xx)); y.Value = Math.Max(1, Math.Min(254, yy)); destinationArea.Value = aa; transitionTail = template.Argument.Substring(12); rows.RemoveAll(p => p.Key.Equals("driver", StringComparison.OrdinalIgnoreCase) || p.Key.Equals("arg", StringComparison.OrdinalIgnoreCase)); }
                SetRows(rows); id.Text = template.Id.Substring(0, Math.Min(34, template.Id.Length)) + "_copy";
            };
            file.TextChanged += (s, e) => CaptureFile(); properties.CellValueChanged += (s, e) => UpdatePreview(); properties.RowsRemoved += (s, e) => UpdatePreview();
            save.Click += (s, e) =>
            {
                try
                {
                    properties.EndEdit(); List<KeyValuePair<string, string>> rows = Rows();
                    if (Transition)
                    {
                        rows.RemoveAll(p => p.Key.Equals("driver", StringComparison.OrdinalIgnoreCase) || p.Key.Equals("arg", StringComparison.OrdinalIgnoreCase));
                        if (!rows.Any(p => p.Key.Equals("flag", StringComparison.OrdinalIgnoreCase) && p.Value == "IF_USE")) rows.Add(new KeyValuePair<string, string>("flag", "IF_USE"));
                        rows.Add(new KeyValuePair<string, string>("driver", "10")); rows.Add(new KeyValuePair<string, string>("arg", TemplateStore.TransitionArgument((int)x.Value, (int)y.Value, (int)destinationArea.Value, transitionTail)));
                    }
                    string target = TemplateStore.Destination(assets.ZonesDirectory, area.Text, file.Text.Trim(), Character);
                    if (target != expectedPath) throw new IOException("Re-enter a valid filename to refresh its source state.");
                    TemplateStore.Save(assets, area.Text, file.Text.Trim(), Character, id.Text.Trim(), rows, expectedText);
                    CreatedId = id.Text.Trim(); CreatedArea = area.Text; CreatedCharacter = Character; DialogResult = DialogResult.OK; Close();
                }
                catch (Exception error) { message.Text = error.Message; }
            };
            RefreshChoices();
            FormClosed += (s, e) => { if (preview.Image != null) preview.Image.Dispose(); };
        }
        private List<KeyValuePair<string, string>> Rows()
        { return properties.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).Select(row => new KeyValuePair<string, string>(Convert.ToString(row.Cells[0].Value).Trim(), Convert.ToString(row.Cells[1].Value))).ToList(); }
        private void SetRows(IEnumerable<KeyValuePair<string, string>> rows) { loading = true; properties.Rows.Clear(); foreach (var row in rows) properties.Rows.Add(row.Key, row.Value); loading = false; UpdatePreview(); }
        private void RefreshChoices()
        {
            loading = true; copy.Items.Clear(); copy.Items.Add("New template"); foreach (Template template in (Character ? assets.AllCharacters : assets.AllItems).Where(t => t.Area == area.Text || t.Area == "generic").OrderBy(t => t.Id)) copy.Items.Add(template); copy.SelectedIndex = 0; file.Text = Character ? "custom.chr" : "custom.itm"; destination.Visible = Transition; loading = false; Defaults(); CaptureFile();
        }
        private void Defaults()
        {
            transitionTail = "01"; var rows = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (key, value) => rows.Add(new KeyValuePair<string, string>(key, value));
            add("name", Character ? "New NPC" : Transition ? type.Text : "New item"); add("description", Character ? "A new inhabitant." : "A new " + type.Text.ToLowerInvariant() + "."); add("sprite", Character ? "1" : type.SelectedIndex == 2 ? "50017" : type.SelectedIndex == 3 ? "50019" : initialSprite.ToString());
            if (Character)
            { add("driver", "7"); add("arg", "aggressive=0;helper=0;scavenger=0;"); add("flag", "CF_RESPAWN"); add("respawn", "60"); foreach (string key in new[] { "V_HP", "V_ENDURANCE", "V_MANA", "V_STR", "V_AGI", "V_INT", "V_WIS" }) add(key, "10"); foreach (string key in new[] { "V_HAND", "V_ATTACK", "V_PARRY" }) add(key, "1"); }
            else { add("flag", Transition ? "IF_USE" : "IF_TAKE"); if (!Transition) add("value", "0"); }
            SetRows(rows); message.Text = "Save appends a new definition and keeps a backup of an existing file.";
        }
        private void CaptureFile()
        {
            try { expectedPath = TemplateStore.Destination(assets.ZonesDirectory, area.Text, file.Text.Trim(), Character); expectedText = File.Exists(expectedPath) ? File.ReadAllText(expectedPath) : null; }
            catch { expectedPath = null; expectedText = null; }
        }
        private void UpdatePreview()
        {
            if (loading) return;
            if (preview.Image != null) { preview.Image.Dispose(); preview.Image = null; }
            int sprite; if (!int.TryParse(Rows().Where(p => p.Key.Equals("sprite", StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).FirstOrDefault(), out sprite)) return;
            SpriteArt art = Character ? assets.NpcArt(assets.Rules.Npc(sprite)) : assets.PreviewArt(sprite); if (art != null) preview.Image = new Bitmap(art.Image);
        }
    }
}

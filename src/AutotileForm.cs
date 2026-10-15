using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Astonia.Editor
{
    public sealed class AutotileForm : Form
    {
        private sealed class Choice
        {
            public string Name;
            public LegacyPreset Pattern;
            public int Manual = -1;
            public bool Stone, Sequential, Sequence;
            public override string ToString() { return Name; }
        }
        public ConnectedTiles Brush;
        public LegacyPreset Pattern;
        private readonly AssetCatalog assets;
        private readonly EditorPreferences preferences;
        private readonly NumericUpDown baseSprite = Number(1, 65535, 12000, 100);
        private readonly NumericUpDown columns = Number(1, 16, 3, 48), rows = Number(1, 16, 3, 48);
        private readonly ComboBox sets = Theme.Combo(660), layer = Theme.Combo(150);
        private readonly CheckBox alternate;
        private readonly Label hint = Theme.Label("", 690, 48);
        private readonly FlowLayoutPanel dimensions = new FlowLayoutPanel { Width = 224, Height = 34, Margin = new Padding(0) };
        private readonly TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(12) };
        private readonly Button apply = Theme.Button("Paint with this set", 190);
        private readonly List<NumericUpDown> values = new List<NumericUpDown>();
        private readonly List<PictureBox> pictures = new List<PictureBox>();
        private int[] walls, grounds;
        private Choice current;
        private LegacyPreset currentPattern;
        private bool updating;

        public AutotileForm(AssetCatalog assets, EditorPreferences preferences, int selectedSprite = 0, int selectedLayer = 0)
        {
            this.assets = assets; this.preferences = preferences;
            walls = (int[])preferences.WallPieces.Clone(); grounds = (int[])preferences.GroundPieces.Clone();
            Text = "Connected autotile set"; ClientSize = new Size(740, 730); MinimumSize = Size;
            BackColor = Theme.Panel; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9); StartPosition = FormStartPosition.CenterParent;
            alternate = Theme.Check("Alternate straight walls", preferences.AlternateStraight);
            baseSprite.Name = "AutotileBase"; sets.Name = "AutotileSet"; layer.Name = "AutotileLayer";
            columns.Name = "AutotileColumns"; rows.Name = "AutotileRows";
            layer.Items.AddRange(MainForm.LayerNames.Take(4).ToArray()); layer.SelectedIndex = Math.Max(0, Math.Min(3, selectedLayer));
            var top = new Panel { Dock = DockStyle.Top, Height = 188, Padding = new Padding(12) };
            var baseRow = new FlowLayoutPanel { Location = new Point(12, 8), Width = 710, Height = 37 };
            baseRow.Controls.Add(Theme.Label("Base sprite", 83, 30)); baseRow.Controls.Add(baseSprite);
            var refill = Theme.Button("Auto-fill set", 130); baseRow.Controls.Add(refill);
            baseRow.Controls.Add(Theme.Label("Uses loaded ground and wall sets.", 340, 32));
            sets.Location = new Point(12, 48); sets.Width = 704;
            var options = new FlowLayoutPanel { Location = new Point(12, 84), Width = 710, Height = 40 };
            options.Controls.Add(Theme.Label("Paint layer", 80, 30)); options.Controls.Add(layer);
            dimensions.Controls.Add(Theme.Label("Size", 36, 30)); dimensions.Controls.Add(columns);
            dimensions.Controls.Add(Theme.Label("×", 18, 30)); dimensions.Controls.Add(rows);
            options.Controls.Add(dimensions); options.Controls.Add(alternate);
            hint.Location = new Point(12, 130);
            top.Controls.Add(baseRow); top.Controls.Add(sets); top.Controls.Add(options); top.Controls.Add(hint);
            apply.Dock = DockStyle.Bottom; apply.Height = 38;
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(table);
            Controls.Add(scroll); Controls.Add(top); Controls.Add(apply);
            baseSprite.ValueChanged += (s, e) => { if (!updating) FindSets(); };
            refill.Click += (s, e) => FindSets();
            sets.SelectedIndexChanged += (s, e) => { if (!updating) SelectSet(); };
            columns.ValueChanged += (s, e) => { if (!updating && current != null && current.Sequential) ShowPieces(); };
            rows.ValueChanged += (s, e) => { if (!updating && current != null && current.Sequential) ShowPieces(); };
            apply.Click += (s, e) => ApplySet();
            FormClosed += (s, e) => DisposePictures();
            updating = true; baseSprite.Value = Math.Max(1, Math.Min(65535, selectedSprite == 0 ? 21000 : selectedSprite)); updating = false;
            FindSets(selectedSprite == 0);
        }
        private static NumericUpDown Number(int min, int max, int value, int width)
        { return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = width, BackColor = Theme.Control, ForeColor = Theme.Text, Margin = new Padding(3, 5, 3, 3) }; }
        private void RememberManual()
        {
            if (current == null || current.Manual < 0 || values.Count != 16) return;
            if (current.Manual == 0) walls = values.Select(v => (int)v.Value).ToArray(); else grounds = values.Select(v => (int)v.Value).ToArray();
        }
        private void FindSets(bool startManual = false)
        {
            RememberManual(); current = null; updating = true; sets.Items.Clear();
            int sprite = (int)baseSprite.Value;
            if (sprite >= 21000 && sprite <= 21015) sets.Items.Add(new Choice { Name = "Stone walls · 16 neighbor connections · 21000–21015", Stone = true });
            foreach (LegacyPreset preset in AutotileSets.Find(assets, sprite))
                sets.Items.Add(new Choice { Name = preset.Name + " · " + (preset.Type == "GROUND" ? preset.GridX + "×" + preset.GridY + " pattern" : preset.Sprites.Count + "-piece wall sequence") + " · " + preset.Area + "/" + System.IO.Path.GetFileName(preset.Source.RelativePath), Pattern = preset });
            int knownCount = sets.Items.Count;
            sets.Items.Add(new Choice { Name = "Sequential ground pattern · fill from base sprite", Sequential = true });
            sets.Items.Add(new Choice { Name = "Sequential wall sequence · fill from base sprite", Sequential = true, Sequence = true });
            sets.Items.Add(new Choice { Name = "Custom wall connections · 16 manual slots", Manual = 0 });
            sets.Items.Add(new Choice { Name = "Custom ground connections · 16 manual slots", Manual = 1 });
            sets.SelectedIndex = startManual ? sets.Items.Count - 2 : 0; updating = false; SelectSet();
            if (knownCount == 0) hint.Text = "No ground/wall preset defines this sprite. Choose the pattern size or wall sequence length; IDs fill consecutively from the base. You can edit every slot.";
        }
        private void SelectSet()
        {
            RememberManual(); current = sets.SelectedItem as Choice; if (current == null) return;
            updating = true;
            if (current.Pattern != null) layer.SelectedIndex = current.Pattern.Affect;
            else if (current.Stone || current.Manual == 0 || current.Sequence) layer.SelectedIndex = 2;
            else layer.SelectedIndex = 0;
            columns.Maximum = current.Sequence ? 256 : 16;
            if (current.Sequence) { columns.Value = 2; rows.Value = 1; } else if (current.Sequential) { columns.Value = 3; rows.Value = 3; }
            updating = false; dimensions.Visible = current.Sequential; rows.Enabled = !current.Sequence;
            alternate.Visible = current.Stone || current.Manual == 0;
            ShowPieces();
        }
        private void DisposePictures()
        { foreach (PictureBox picture in pictures) if (picture.Image != null) picture.Image.Dispose(); pictures.Clear(); }
        private void ShowPieces()
        {
            apply.Enabled = false; currentPattern = null;
            DisposePictures(); values.Clear(); foreach (Control control in table.Controls.Cast<Control>().ToArray()) control.Dispose(); table.Controls.Clear();
            int[] pieces;
            try
            {
                if (current.Sequential) currentPattern = AutotileSets.Sequential((int)baseSprite.Value, (int)columns.Value, (int)rows.Value, layer.SelectedIndex, current.Sequence);
                else if (current.Pattern != null) currentPattern = AutotileSets.SpritePattern(current.Pattern, layer.SelectedIndex);
                pieces = currentPattern != null ? currentPattern.Sprites.ToArray() : current.Stone ? (int[])ConnectedTiles.DefaultWalls.Clone() : current.Manual == 0 ? walls : grounds;
            }
            catch (ArgumentOutOfRangeException ex) { hint.Text = ex.Message; return; }
            int width = currentPattern != null && currentPattern.Type == "GROUND" ? Math.Min(6, currentPattern.GridX) : 4;
            table.SuspendLayout(); table.ColumnCount = width; table.RowCount = (pieces.Length + width - 1) / width; table.ColumnStyles.Clear(); table.RowStyles.Clear();
            for (int i = 0; i < width; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / width));
            for (int i = 0; i < table.RowCount; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            table.Height = table.RowCount * 120 + table.Padding.Vertical;
            for (int i = 0; i < pieces.Length; i++)
            {
                string text = currentPattern == null ? ConnectedTiles.Labels[i] : currentPattern.Type == "GROUND" ? "Tile " + (i % currentPattern.GridX + 1) + ", " + (i / currentPattern.GridX + 1) : "Sequence " + (i + 1);
                var cell = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3) };
                var label = Theme.Label(text, 190, 28); label.Dock = DockStyle.Top;
                var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Control };
                var value = Number(currentPattern == null ? 1 : 0, 65535, pieces[i], 130); value.Dock = DockStyle.Bottom; value.Tag = "AutotilePiece";
                Action refresh = () => { if (picture.Image != null) { picture.Image.Dispose(); picture.Image = null; } SpriteArt art = assets.PreviewArt((int)value.Value); picture.Image = art == null ? null : new Bitmap(art.Image); label.ForeColor = art == null ? Color.IndianRed : Theme.Text; };
                value.ValueChanged += (s, e) => refresh(); refresh(); values.Add(value); pictures.Add(picture);
                cell.Controls.Add(picture); cell.Controls.Add(label); cell.Controls.Add(value); table.Controls.Add(cell, i % width, i / width);
            }
            table.ResumeLayout();
            hint.Text = currentPattern == null ? "Neighbors reconnect as you paint. SE = x+1, SW = y+1, NW = x−1, NE = y−1. Every slot is editable." :
                (currentPattern.Type == "GROUND" ? currentPattern.GridX + "×" + currentPattern.GridY + " repeating pattern, aligned to map coordinates." : "Wall variations follow adjoining sequence pieces.") + " " + pieces.Length + " slots auto-filled. Existing tile flags stay unchanged.";
            apply.Enabled = true;
        }
        private void ApplySet()
        {
            RememberManual(); preferences.WallPieces = walls; preferences.GroundPieces = grounds; preferences.AlternateStraight = alternate.Checked;
            if (currentPattern != null) { Pattern = AutotileSets.SpritePattern(currentPattern, layer.SelectedIndex); Pattern.Sprites = values.Select(v => (int)v.Value).ToList(); }
            else Brush = new ConnectedTiles { Pieces = values.Select(v => (int)v.Value).ToArray(), AlternateStraight = alternate.Visible && alternate.Checked, Layer = layer.SelectedIndex };
            DialogResult = DialogResult.OK; Close();
        }
    }
}

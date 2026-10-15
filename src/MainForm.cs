using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Astonia.Editor
{
    public sealed class MainForm : Form
    {
        public static readonly string[] LayerNames = { "Ground 1", "Ground 2", "Foreground 1", "Foreground 2", "Item", "NPC", "Flags", "Whole tile" };
        private readonly EditorPreferences preferences = EditorPreferences.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "editor-settings.json"));
        private MapDocument zoneBaseline;
        private Tile inspectorBase;
        private readonly ComboBox presetType = Theme.Combo(135);
        private readonly Button apply = Theme.Button("Apply to selected tile", 246);
        private readonly CheckBox decorativeDoors = Theme.Check("Decorative door pictures");
        private HashSet<string> doorFilter;
        public readonly MapCanvas Canvas = new MapCanvas();
        private AssetCatalog assets = new AssetCatalog();
        private readonly SpriteGrid library = new SpriteGrid();
        private readonly MiniMap mini = new MiniMap();
        private readonly ComboBox maps = Theme.Combo(210), category = Theme.Combo(224), layer = Theme.Combo(130), brushSize = Theme.Combo(64), flagOperation = Theme.Combo(100);
        private readonly TextBox search = Theme.TextBox(224), goTo = Theme.TextBox(72);
        private readonly ListBox templates = new ListBox();
        private readonly ListBox presets = new ListBox();
        private readonly NumericUpDown brushId = Number(65535), g1 = Number(65535), g2 = Number(65535), f1 = Number(65535), f2 = Number(65535);
        private readonly ComboBox item = Theme.Combo(246), npc = Theme.Combo(246);
        private readonly CheckedListBox tileFlags = new CheckedListBox(), brushFlags = new CheckedListBox();
        private readonly Dictionary<EditorTool, Button> toolButtons = new Dictionary<EditorTool, Button>();
        private readonly Label fileLabel = Theme.Label("No map open", 280), status = Theme.Label("Ready", 780), zoomLabel = Theme.Label("200%", 46), selectionLabel = Theme.Label("Select a tile to inspect", 246), brushLabel = Theme.Label("Ground 1  ·  12000", 224);
        private readonly Button undo = Theme.Button("Undo", 54), redo = Theme.Button("Redo", 54), save = Theme.Button("Save copy", 96);
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer searchTimer = new Timer { Interval = 180 };
        private bool populating;
        private string workspace, initialPath;
        private Panel center;
        public MainForm(string mapPath, string workspacePath)
        {
            initialPath = mapPath; workspace = workspacePath;
            Rectangle screen = Screen.AllScreens.OrderByDescending(display => display.WorkingArea.Width).First().WorkingArea;
            Text = "Astonia World Editor"; ClientSize = new Size(Math.Min(1480, screen.Width - 60), Math.Min(900, screen.Height - 90)); MinimumSize = new Size(1000, 720);
            StartPosition = FormStartPosition.Manual; Location = new Point(screen.X + (screen.Width - Width) / 2, screen.Y + (screen.Height - Height) / 2);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); BackColor = Theme.Panel; ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9); KeyPreview = true; AutoScaleMode = AutoScaleMode.Dpi;
            BuildLayout(); WireEvents();
            Canvas.FlagOverlay.Visible = new HashSet<string>(preferences.VisibleFlags, StringComparer.OrdinalIgnoreCase);
            Shown += (s, e) => InitializeWorkspace();
            FormClosing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; else SavePreferences(); };
        }
        private static NumericUpDown Number(int maximum)
        { return new NumericUpDown { Minimum = 0, Maximum = maximum, Width = 112, BackColor = Theme.Control, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle }; }
        private static FlowLayoutPanel Flow(int height)
        { return new FlowLayoutPanel { Dock = DockStyle.Top, Height = height, BackColor = Theme.Panel, Padding = new Padding(7, 2, 7, 1), WrapContents = false }; }
        private static void FitFlow(FlowLayoutPanel panel, int minimum)
        { panel.WrapContents = true; panel.Layout += (s, e) => { if (panel.Controls.Count > 0) { int height = Math.Max(minimum, panel.Controls.Cast<Control>().Max(c => c.Bottom + c.Margin.Bottom) + panel.Padding.Bottom); if (panel.Height != height) panel.Height = height; } }; }
        private void BuildLayout()
        {
            var header = Flow(58); header.Padding = new Padding(12, 9, 12, 5); header.WrapContents = true;
            FitFlow(header, 58);
            var brand = new Label { Text = "ASTONIA\nWORLD EDITOR", ForeColor = Theme.Gold, Font = new Font("Segoe UI Semibold", 10), Width = 153, Height = 42 };
            header.Controls.Add(brand);
            header.Controls.Add(maps);
            var openSelected = Theme.Button("Open zone", 86); openSelected.Click += (s, e) => OpenSelectedMap(); header.Controls.Add(openSelected);
            var open = Theme.Button("Open .map", 88); open.Click += (s, e) => OpenFile(); header.Controls.Add(open);
            var create = Theme.Button("New", 56); create.Click += (s, e) => NewMap(); header.Controls.Add(create);
            var sources = Theme.Button("Sources", 72); sources.Click += (s, e) => ConfigureSources(); header.Controls.Add(sources);
            var files = Theme.Button("Files", 54); files.Click += (s, e) => ShowSourceBrowser(); header.Controls.Add(files);
            var export = Theme.Button("PNG view", 88); export.Click += (s, e) => ExportPng(); header.Controls.Add(export);
            var snapshot = Theme.Button("Snapshot PNG", 107); snapshot.Click += (s, e) => ExportSnapshot(); header.Controls.Add(snapshot);
            var saveZone = Theme.Button("Save zone files", 115); saveZone.Click += (s, e) => SaveZoneFiles(); header.Controls.Add(saveZone);
            header.Controls.Add(save); save.ForeColor = Theme.Gold; save.Click += (s, e) => SaveMap(false);
            var saveAs = Theme.Button("Save as", 72); saveAs.Click += (s, e) => SaveMap(true); header.Controls.Add(saveAs);
            var help = Theme.Button("?", 32); help.Click += (s, e) => ShowHelp(); header.Controls.Add(help);
            tips.SetToolTip(sources, "Choose a project, graphics folder, or zones folder. No game process is needed.");
            tips.SetToolTip(save, "Ctrl+S. The first save creates a working copy. Subsequent saves keep timestamped backups.");

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Control, Padding = new Padding(10, 2, 10, 0) };
            status.Dock = DockStyle.Fill; footer.Controls.Add(status);
            var version = Theme.Label("EDITOR 0.3.2  ·  OFFLINE", 205); version.Dock = DockStyle.Right; version.TextAlign = ContentAlignment.MiddleRight; footer.Controls.Add(version);

            var left = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = Theme.Panel, Padding = new Padding(12, 10, 12, 10) };
            var leftTitle = Theme.Label("ASSET LIBRARY", 224, 27); leftTitle.ForeColor = Theme.Gold; leftTitle.Dock = DockStyle.Top;
            var searchArea = new Panel { Dock = DockStyle.Top, Height = 37 }; search.Dock = DockStyle.Top; searchArea.Controls.Add(search);
            tips.SetToolTip(search, "Search sprite IDs or item/NPC template names. Searching keeps the selected category.");
            search.AccessibleName = "Search assets";
            var categoryArea = new Panel { Dock = DockStyle.Top, Height = 37 }; category.Dock = DockStyle.Top; categoryArea.Controls.Add(category);
            category.Items.AddRange(new object[] { "Ground tiles", "Walls & buildings", "Details & objects", "All world artwork", "Used in this map", "Item templates · all areas", "NPC templates · all areas", "Presets · all files", "Autotiles · original files", "Ground overlays · Ground 2", "Door artwork", "Interactive doors", "Scenery & furniture", "Item artwork", "Effects & overlays", "Client variants", "All PNG images · preview" }); category.SelectedIndex = 0;
            var libraryFilters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, WrapContents = true };
            FitFlow(libraryFilters, 76);
            presetType.Items.AddRange(new[] { "All preset types", "PRESET", "CHANCE", "GROUND", "LINEWALL" }); presetType.SelectedIndex = 0; libraryFilters.Controls.Add(presetType);
            var connected = Theme.Button("Connected set…", 160); connected.Click += (s, e) => ConfigureAutotile(); libraryFilters.Controls.Add(connected); libraryFilters.Controls.Add(decorativeDoors);
            var libraryTabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false };
            foreach (var tab in new[] { new { Text = "Sprites", Index = 0, Width = 66 }, new { Text = "Presets", Index = 7, Width = 66 }, new { Text = "Autotiles", Index = 8, Width = 77 } })
            { var button = Theme.Button(tab.Text, tab.Width); button.Click += (s, e) => category.SelectedIndex = tab.Index; libraryTabs.Controls.Add(button); }
            var libraryBody = new Panel { Dock = DockStyle.Fill };
            library.Dock = DockStyle.Fill; templates.Dock = DockStyle.Fill; templates.BackColor = Theme.Control; templates.ForeColor = Theme.Text; templates.BorderStyle = BorderStyle.None; templates.IntegralHeight = false; templates.HorizontalScrollbar = true; templates.Visible = false;
            libraryBody.Controls.Add(library); libraryBody.Controls.Add(templates);
            presets.Dock = DockStyle.Fill; presets.BackColor = Theme.Control; presets.ForeColor = Theme.Text; presets.BorderStyle = BorderStyle.None; presets.IntegralHeight = false; presets.Visible = false; presets.HorizontalScrollbar = true;
            presets.DrawMode = DrawMode.OwnerDrawFixed; presets.ItemHeight = 68; presets.DrawItem += DrawPreset; libraryBody.Controls.Add(presets);
            templates.DrawMode = DrawMode.OwnerDrawFixed; templates.ItemHeight = 58; templates.DrawItem += DrawTemplate;
            var brushPanel = new Panel { Dock = DockStyle.Bottom, Height = 197, Padding = new Padding(0, 8, 0, 0) };
            var brushTitle = Theme.Label("ACTIVE BRUSH", 224, 26); brushTitle.ForeColor = Theme.Gold; brushTitle.Location = new Point(0, 8); brushPanel.Controls.Add(brushTitle);
            brushLabel.Location = new Point(0, 36); brushPanel.Controls.Add(brushLabel);
            var idLabel = Theme.Label("Sprite ID", 74); idLabel.Location = new Point(0, 64); brushPanel.Controls.Add(idLabel);
            brushId.Location = new Point(81, 64); brushId.Value = 12000; brushPanel.Controls.Add(brushId);
            var note = Theme.Label("B  Paint     R  Rectangle     F  Fill\nE  Erase     P  Pick     S  Select\nCtrl+C / V  Copy / paste area\nRight drag or Space  Pan\nWheel  Zoom     F8  Lower walls", 224, 95); note.Location = new Point(0, 98); brushPanel.Controls.Add(note);
            left.Controls.Add(libraryBody); left.Controls.Add(brushPanel); left.Controls.Add(libraryFilters); left.Controls.Add(categoryArea); left.Controls.Add(libraryTabs); left.Controls.Add(searchArea); left.Controls.Add(leftTitle);

            var right = new Panel { Dock = DockStyle.Right, Width = 292, BackColor = Theme.Panel, Padding = new Padding(12, 10, 12, 0), AutoScroll = true };
            var inspector = new Panel { Dock = DockStyle.Top, Height = 536 };
            int y = 0;
            AddAt(inspector, Theme.Label("TILE INSPECTOR", 246, 27), 0, y); y += 27;
            selectionLabel.ForeColor = Theme.Gold; AddAt(inspector, selectionLabel, 0, y); y += 29;
            AddAt(inspector, Theme.Label("Ground 1", 113), 0, y); AddAt(inspector, Theme.Label("Ground 2", 113), 130, y); y += 23;
            AddAt(inspector, g1, 0, y); AddAt(inspector, g2, 130, y); y += 29;
            g1.AccessibleName = "Ground 1 sprite"; g2.AccessibleName = "Ground 2 sprite";
            AddAt(inspector, Theme.Label("Foreground 1", 113), 0, y); AddAt(inspector, Theme.Label("Foreground 2", 113), 130, y); y += 23;
            AddAt(inspector, f1, 0, y); AddAt(inspector, f2, 130, y); y += 29;
            f1.AccessibleName = "Foreground 1 sprite"; f2.AccessibleName = "Foreground 2 sprite";
            AddAt(inspector, Theme.Label("Item template", 246), 0, y); y += 23; PrepareTemplateCombo(item); AddAt(inspector, item, 0, y); y += 31;
            AddAt(inspector, Theme.Label("NPC template", 246), 0, y); y += 23; PrepareTemplateCombo(npc); AddAt(inspector, npc, 0, y); y += 36;
            var newTemplate = Theme.Button("New item / NPC template", 246); AddAt(inspector, newTemplate, 0, y); y += 38; newTemplate.Click += (s, e) => CreateTemplate();
            var visibility = Theme.Button("Flag visibility & colors", 246); AddAt(inspector, visibility, 0, y); y += 38; visibility.Click += (s, e) => { using (var dialog = new FlagOptionsForm(Canvas)) dialog.ShowDialog(this); foreach (CheckBox check in FindAllControls(this).OfType<CheckBox>()) if (check.Text == "Flags") check.Checked = Canvas.ShowFlags; SavePreferences(); };
            var tileFlagTitle = Theme.Label("TILE FLAGS", 246, 23); AddAt(inspector, tileFlagTitle, 0, y); y += 23;
            PrepareFlagList(tileFlags); tileFlags.Size = new Size(246, 112); AddAt(inspector, tileFlags, 0, y); y += 119;
            AddAt(inspector, apply, 0, y); apply.Click += (s, e) => ApplyInspector();
            tips.SetToolTip(apply, "Apply all inspector values as a single undoable edit. Unknown map directives are retained.");

            var flagPanel = new Panel { Dock = DockStyle.Top, Height = 164 };
            AddAt(flagPanel, Theme.Label("FLAG BRUSH", 105, 26), 0, 2);
            flagOperation.Items.AddRange(new object[] { "Add", "Remove", "Replace" }); flagOperation.SelectedIndex = 0; AddAt(flagPanel, flagOperation, 138, 0);
            PrepareFlagList(brushFlags); brushFlags.Size = new Size(246, 86); AddAt(flagPanel, brushFlags, 0, 35);
            var useFlags = Theme.Button("Paint flags", 246); AddAt(flagPanel, useFlags, 0, 124); useFlags.Click += (s, e) => { layer.SelectedIndex = 6; Canvas.FlagOverlay.Visible.UnionWith(Canvas.BrushFlags.Where(flag => flag != "MF_NEUTRAL")); foreach (CheckBox check in FindAllControls(this).OfType<CheckBox>()) if (check.Text == "Flags") check.Checked = true; SetTool(EditorTool.Paint); Say("Flag brush ready. Choose flags above, then paint the map."); };

            var miniPanel = new Panel { Dock = DockStyle.Top, Height = 187 }; AddAt(miniPanel, Theme.Label("NAVIGATOR  ·  click to travel", 246, 25), 0, 0);
            mini.Location = new Point(0, 27); mini.Size = new Size(246, 154); mini.Canvas = Canvas; miniPanel.Controls.Add(mini);
            right.Controls.Add(miniPanel); right.Controls.Add(flagPanel); right.Controls.Add(inspector);

            center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas, Padding = new Padding(1, 0, 1, 0) };
            var toolbar = Flow(40); FitFlow(toolbar, 40);
            var names = new[] { "Inspect", "Paint", "Rect", "Fill", "Erase", "Pick", "Select", "Pan" };
            for (int i = 0; i < names.Length; i++) { EditorTool tool = (EditorTool)i; Button b = Theme.Button(names[i], i == 0 ? 62 : 51); b.Click += (s, e) => SetTool(tool); toolButtons[tool] = b; toolbar.Controls.Add(b); }
            toolbar.Controls.Add(undo); toolbar.Controls.Add(redo);
            var options = Flow(38); FitFlow(options, 38);
            layer.AccessibleName = "Active layer"; layer.Items.AddRange(LayerNames); layer.SelectedIndex = 0; options.Controls.Add(layer);
            brushSize.Items.AddRange(new object[] { "1 x 1", "2 x 2", "3 x 3", "4 x 4", "5 x 5", "8 x 8", "9 x 9" }); brushSize.SelectedIndex = 0; options.Controls.Add(brushSize);
            AddToggle(options, "Grid", false, value => Canvas.ShowGrid = value);
            AddToggle(options, "Flags", false, value => Canvas.ShowFlags = value);
            AddToggle(options, "Objects", true, value => Canvas.ShowForeground = value);
            AddToggle(options, "Markers", true, value => Canvas.ShowMarkers = value);
            AddToggle(options, "Low walls", false, value => Canvas.LowerWalls = value, "F8");
            AddToggle(options, "NPCs", true, value => Canvas.ShowNpcs = value);
            AddToggle(options, "Sight", false, value => Canvas.ShowSight = value, "Sight");
            AddToggle(options, "Show spikes", true, value => Canvas.ShowSpikes = value);
            AddToggle(options, "Auto walls", true, value => Canvas.AutoWalls = value, "AutoWalls");
            AddToggle(options, "Collision", false, value => Canvas.ShowCollision = value);
            AddToggle(options, "Enhanced sprites", true, value => Canvas.EnhancedSprites = value);
            var sightRange = Number(64); sightRange.Minimum = 2; sightRange.Value = 20; sightRange.Width = 43; sightRange.Margin = new Padding(3, 7, 3, 3); sightRange.AccessibleName = "Sight preview range"; options.Controls.Add(sightRange);
            sightRange.ValueChanged += (s, e) => { Canvas.SightRange = (int)sightRange.Value; Canvas.Invalidate(); };
            tips.SetToolTip(sightRange, "Sight range in tiles. The observer follows the hovered tile; sight-blocking tiles are outlined S / TS.");
            var navigation = Flow(34); FitFlow(navigation, 34);
            fileLabel.ForeColor = Theme.Text; navigation.Controls.Add(fileLabel);
            goTo.Margin = new Padding(3, 5, 3, 3); goTo.AccessibleName = "Go to coordinates"; navigation.Controls.Add(goTo); tips.SetToolTip(goTo, "Enter x,y from 0 to 255, then press Enter or Go.");
            var go = Theme.Button("Go", 39); go.Height = 26; go.Click += (s, e) => GoTo(); navigation.Controls.Add(go);
            var home = Theme.Button("Center", 61); home.Height = 26; home.Click += (s, e) => Canvas.CenterOn(128, 128); navigation.Controls.Add(home);
            var fit = Theme.Button("Fit", 39); fit.Height = 26; fit.Click += (s, e) => Canvas.Fit(); navigation.Controls.Add(fit);
            var native = Theme.Button("1:1", 39); native.Height = 26; native.Click += (s, e) => Canvas.ZoomAt(1, new Point(Canvas.Width / 2, Canvas.Height / 2)); navigation.Controls.Add(native);
            navigation.Controls.Add(zoomLabel); zoomLabel.Height = 28;
            Canvas.Dock = DockStyle.Fill; center.Controls.Add(Canvas); center.Controls.Add(navigation); center.Controls.Add(options); center.Controls.Add(toolbar);
            Controls.Add(center); Controls.Add(right); Controls.Add(left); Controls.Add(footer); Controls.Add(header);
            SetTool(EditorTool.Inspect);
        }
        private static void AddAt(Control parent, Control child, int x, int y) { child.Location = new Point(x, y); parent.Controls.Add(child); }
        private static void PrepareTemplateCombo(ComboBox combo) { combo.DropDownStyle = ComboBoxStyle.DropDown; combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend; combo.AutoCompleteSource = AutoCompleteSource.ListItems; }
        private static void PrepareFlagList(CheckedListBox list)
        {
            list.BackColor = Theme.Control; list.ForeColor = Theme.Text; list.BorderStyle = BorderStyle.None; list.CheckOnClick = true; list.IntegralHeight = false;
            foreach (FlagInfo flag in EditorFlags.All) list.Items.Add(flag);
        }
        private void AddToggle(Control parent, string text, bool value, Action<bool> setter, string shortcut = null)
        {
            var check = Theme.Check(text, value); check.CheckedChanged += (s, e) => { setter(check.Checked); Canvas.Invalidate(); }; parent.Controls.Add(check);
            if (shortcut != null) { check.Tag = shortcut; tips.SetToolTip(check, shortcut == "Sight" ? "Preview sight from the hovered tile. S: map sight block; TS: temporary/item sight block. The dashed line targets the selected tile." : shortcut + ". View only; saved map values stay intact."); }
        }
        private void ShowSourceBrowser()
        {
            using (var browser = new SourceBrowser(assets)) if (browser.ShowDialog(this) == DialogResult.OK && browser.SelectedMap != null) OpenMap(browser.SelectedMap);
        }
        private void DrawPreset(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= presets.Items.Count) return;
            LegacyPreset preset = (LegacyPreset)presets.Items[e.Index];
            DrawLibraryEntry(e, preset.PreviewSprite, preset.Name + " · " + preset.Type, preset.Area + "/" + Path.GetFileName(preset.Source.RelativePath), preset.Error == "" ? Theme.Text : Color.FromArgb(235, 130, 130));
        }
        private void DrawTemplate(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= templates.Items.Count) return;
            Template record = (Template)templates.Items[e.Index];
            int frame = record.Character ? assets.Rules.Npc(record.Sprite, record.Direction).Frame : record.Sprite;
            DrawLibraryEntry(e, frame, record.Id, record.Area + "/" + Path.GetFileName(record.Source.RelativePath), Theme.Text, record.Character, record.Character ? assets.Rules.Npc(record.Sprite, record.Direction).Effects : null);
        }
        private void DrawLibraryEntry(DrawItemEventArgs e, int sprite, string name, string source, Color color, bool resolvedFrame = false, SpriteEffects effects = null)
        {
            using (var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Active : Theme.Control)) e.Graphics.FillRectangle(brush, e.Bounds);
            SpriteArt art = resolvedFrame ? assets.Art(sprite, 1, effects) : assets.PreviewArt(sprite);
            if (art != null)
            {
                float factor = Math.Min(48f / art.Image.Width, (e.Bounds.Height - 8f) / art.Image.Height);
                int width = Math.Max(1, (int)(art.Image.Width * factor)), height = Math.Max(1, (int)(art.Image.Height * factor));
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                e.Graphics.DrawImage(art.Image, new Rectangle(e.Bounds.X + (52 - width) / 2, e.Bounds.Y + (e.Bounds.Height - height) / 2, width, height));
            }
            TextRenderer.DrawText(e.Graphics, name, Font, new Rectangle(e.Bounds.X + 57, e.Bounds.Y + 8, e.Bounds.Width - 60, 23), color, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(e.Graphics, source, Font, new Rectangle(e.Bounds.X + 57, e.Bounds.Y + 30, e.Bounds.Width - 60, 22), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
        private void WireEvents()
        {
            Canvas.Changed += () => { UpdateDocumentState(); LoadInspector(); mini.RefreshMap(); if (category.SelectedIndex == 4) RefreshLibrary(); };
            Canvas.TileSelected += LoadInspector;
            Canvas.ViewChanged += () => { zoomLabel.Text = Math.Round(Canvas.Zoom * 100) + "%"; mini.Invalidate(); };
            Canvas.Status += Say;
            Canvas.DoorPictureRequested += SelectSprite;
            Canvas.BrushPicked += () => { populating = true; layer.SelectedIndex = Canvas.Layer; brushId.Value = Canvas.BrushSprite; SetChecked(brushFlags, Canvas.BrushFlags); populating = false; if (Canvas.Layer == 7) SetTool(EditorTool.Paint); UpdateBrushLabel(); };
            library.Picked += SelectSprite;
            templates.SelectedIndexChanged += (s, e) =>
            {
                Template t = templates.SelectedItem as Template; if (t == null) return;
                Template resolved; if (!(t.Character ? assets.Characters : assets.Items).TryGetValue(t.Id, out resolved) || resolved.Source != t.Source)
                { Say(t.Id + " is loaded from area " + t.Area + ". Open that area's map to use this definition; the current map uses area " + assets.ActiveArea + "."); return; }
                Canvas.PresetBrush = null; Canvas.ConnectedBrush = null; layer.SelectedIndex = t.Character ? 5 : 4; Canvas.BrushTemplate = t.Id; SetTool(EditorTool.Paint); UpdateBrushLabel();
            };
            presets.SelectedIndexChanged += (s, e) =>
            {
                LegacyPreset preset = presets.SelectedItem as LegacyPreset; if (preset == null) return; string reason = preset.UnavailableReason(assets);
                if (reason != "") { Say(preset.Name + ": " + reason + " See Files for the source definition."); return; }
                layer.SelectedIndex = preset.Type == "PRESET" ? Enumerable.Range(0, 6).FirstOrDefault(i => preset.Enabled[i]) : preset.Affect;
                Canvas.PresetBrush = preset; Canvas.ConnectedBrush = null; SetTool(EditorTool.Paint); UpdateBrushLabel(); Say(preset.Name + " · " + preset.Type + " · " + preset.Source.DisplayPath + ". Paint or drag a rectangle to apply.");
            };
            layer.SelectedIndexChanged += (s, e) =>
            {
                Canvas.PresetBrush = null; Canvas.ConnectedBrush = null; Canvas.Layer = layer.SelectedIndex;
                if (Canvas.Layer == 4 || Canvas.Layer == 5)
                {
                    Template t = templates.SelectedItem as Template, resolved;
                    Canvas.BrushTemplate = t != null && t.Character == (Canvas.Layer == 5) && (t.Character ? assets.Characters : assets.Items).TryGetValue(t.Id, out resolved) && resolved.Source == t.Source ? t.Id : "";
                }
                UpdateBrushLabel();
            };
            brushSize.SelectedIndexChanged += (s, e) => Canvas.BrushSize = new[] { 1, 2, 3, 4, 5, 8, 9 }[brushSize.SelectedIndex];
            brushId.ValueChanged += (s, e) => { if (populating) return; Canvas.PresetBrush = null; Canvas.ConnectedBrush = null; Canvas.BrushSprite = (int)brushId.Value; library.SelectedId = Canvas.BrushSprite; library.Invalidate(); UpdateBrushLabel(); };
            flagOperation.SelectedIndexChanged += (s, e) => Canvas.FlagOperation = flagOperation.Text;
            brushFlags.ItemCheck += (s, e) => { if (populating) return; BeginInvoke((Action)(() => { Canvas.BrushFlags = GetChecked(brushFlags); UpdateBrushLabel(); })); };
            category.SelectedIndexChanged += (s, e) => { Canvas.PresetBrush = null; Canvas.ConnectedBrush = null; int selected = category.SelectedIndex; if (selected == 0) layer.SelectedIndex = 0; if (selected == 9) layer.SelectedIndex = 1; if (selected == 1 || selected == 10 || selected >= 12 && selected <= 14) layer.SelectedIndex = 2; if (selected == 5 || selected == 6 || selected == 11) layer.SelectedIndex = selected == 6 ? 5 : 4; RefreshLibrary(); UpdateBrushLabel(); };
            presetType.SelectedIndexChanged += (s, e) => RefreshLibrary();
            decorativeDoors.CheckedChanged += (s, e) => Canvas.DecorativeDoors = decorativeDoors.Checked;
            search.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); RefreshLibrary(); };
            undo.Click += (s, e) => { if (Canvas.Document != null) { Canvas.Document.Undo(); Canvas.NotifyChanged(); } };
            redo.Click += (s, e) => { if (Canvas.Document != null) { Canvas.Document.Redo(); Canvas.NotifyChanged(); } };
            goTo.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { GoTo(); e.SuppressKeyPress = true; } };
            KeyDown += OnEditorKeyDown; KeyUp += (s, e) => { if (e.KeyCode == Keys.Space) Canvas.SpacePan(false); };
            Deactivate += (s, e) => Canvas.SpacePan(false);
        }
        private void InitializeWorkspace()
        {
            try
            {
                if (workspace == null && !LoadSourceSettings()) workspace = AssetCatalog.FindWorkspace(AppDomain.CurrentDomain.BaseDirectory);
                if (workspace != null) SetWorkspace(workspace);
                if (initialPath != null) OpenMap(initialPath);
                else if (maps.Items.Count > 0) OpenSelectedMap();
                else Say("Open Sources to choose your graphics and zones, or create a new map.");
            }
            catch (Exception ex) { Error("Could not initialize the editor", ex); }
        }
        private void SetWorkspace(string root)
        {
            string graphics = Path.Combine(root, "client", "res"), zones = Path.Combine(root, "server", "zones");
            if (!Directory.Exists(graphics) || !Directory.Exists(zones)) throw new IOException("Choose the project folder containing client/res and server/zones, or configure those folders separately.");
            var next = new AssetCatalog { Workspace = root, ZonesDirectory = zones }; next.LoadGraphics(graphics); next.LoadZones(zones);
            assets.Dispose(); assets = next; workspace = root; Canvas.Assets = library.Assets = assets; RefreshMaps(); ReloadTemplates(); RefreshLibrary(); Canvas.Invalidate();
        }
        private void RefreshMaps()
        {
            maps.Items.Clear(); if (assets.ZonesDirectory == null || !Directory.Exists(assets.ZonesDirectory)) return;
            foreach (SourceFile file in assets.MapFiles.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)) maps.Items.Add(file.RelativePath);
            if (maps.Items.Count > 0) { int index = maps.Items.IndexOf("1/cameron.map"); maps.SelectedIndex = index >= 0 ? index : 0; }
        }
        private void ConfigureSources()
        {
            using (var dialog = new Form { Text = "Editor sources", ClientSize = new Size(560, 245), BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false })
            {
                var graphics = Theme.TextBox(435); graphics.Text = assets.GraphicsDirectory ?? "";
                var zones = Theme.TextBox(435); zones.Text = assets.ZonesDirectory ?? "";
                AddAt(dialog, Theme.Label("Graphics folder  ·  contains gx1.zip / patches / mods", 510), 16, 14); AddAt(dialog, graphics, 16, 43);
                AddAt(dialog, Theme.Label("Zones folder  ·  numbered map and template folders", 510), 16, 84); AddAt(dialog, zones, 16, 113);
                foreach (var pair in new[] { new { Box = graphics, Y = 40 }, new { Box = zones, Y = 110 } })
                {
                    var browse = Theme.Button("…", 65); AddAt(dialog, browse, 466, pair.Y);
                    browse.Click += (s, e) => { using (var f = new FolderBrowserDialog { Description = "Select the source folder", SelectedPath = pair.Box.Text, ShowNewFolderButton = false }) if (f.ShowDialog(dialog) == DialogResult.OK) pair.Box.Text = f.SelectedPath; };
                }
                var load = Theme.Button("Load sources", 120); AddAt(dialog, load, 411, 183);
                load.Click += (s, e) =>
                {
                    AssetCatalog next = null;
                    try
                    {
                        if (!Directory.Exists(graphics.Text) || !Directory.Exists(zones.Text)) throw new IOException("Both source folders must exist.");
                        next = new AssetCatalog { ZonesDirectory = zones.Text }; next.LoadGraphics(graphics.Text);
                        next.LoadZones(zones.Text);
                        if (next.Ids.Count == 0) throw new IOException("No sprite PNGs were found. Choose a folder containing gx1.zip or numbered PNGs.");
                        assets.Dispose(); assets = next; Canvas.PresetBrush = null; Canvas.Assets = library.Assets = assets; RefreshMaps(); ReloadTemplates(); RefreshLibrary(); mini.RefreshMap(); Canvas.NotifyChanged();
                        SaveSourceSettings(); Say(assets.Summary); dialog.DialogResult = DialogResult.OK; dialog.Close();
                    }
                    catch (Exception ex) { if (next != null && next != assets) next.Dispose(); Error("Could not load sources", ex); }
                };
                dialog.ShowDialog(this);
            }
        }
        private bool LoadSourceSettings()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sources.ini");
            if (!File.Exists(path)) return false;
            try
            {
                string graphics = null, zones = null;
                foreach (string line in File.ReadAllLines(path)) { if (line.StartsWith("graphics=")) graphics = line.Substring(9); if (line.StartsWith("zones=")) zones = line.Substring(6); }
                if (!Directory.Exists(graphics) || !Directory.Exists(zones)) return false;
                var next = new AssetCatalog { ZonesDirectory = zones }; next.LoadGraphics(graphics); next.LoadZones(zones);
                if (next.Ids.Count == 0) { next.Dispose(); return false; }
                assets.Dispose(); assets = next; Canvas.Assets = library.Assets = assets; RefreshMaps(); ReloadTemplates(); RefreshLibrary(); return true;
            }
            catch { return false; }
        }
        private void SaveSourceSettings()
        {
            try { File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sources.ini"), new[] { "graphics=" + assets.GraphicsDirectory, "zones=" + assets.ZonesDirectory }); }
            catch (IOException) { Say("Sources loaded for this session; the app folder is read-only."); }
            catch (UnauthorizedAccessException) { Say("Sources loaded for this session; the app folder is read-only."); }
        }
        private void ReloadTemplates()
        {
            string area = Canvas.Document == null ? assets.ActiveArea : Canvas.Document.AreaContext ?? (Canvas.Document.SourcePath == null ? assets.ActiveArea : assets.AreaForMap(Canvas.Document.SourcePath));
            assets.LoadTemplates(area); string itemText = item.Text, npcText = npc.Text;
            item.Items.Clear(); npc.Items.Clear(); item.Items.Add(""); npc.Items.Add("");
            item.Items.AddRange(assets.Items.Keys.OrderBy(i => i).Cast<object>().ToArray()); npc.Items.AddRange(assets.Characters.Keys.OrderBy(i => i).Cast<object>().ToArray());
            item.Text = itemText; npc.Text = npcText;
        }
        private void RefreshLibrary()
        {
            if (assets == null) return; string query = search.Text.Trim(); int selected = category.SelectedIndex;
            library.RawImages = selected == 16;
            bool isTemplate = selected == 5 || selected == 6 || selected == 11, isPreset = selected == 7 || selected == 8; library.Visible = !isTemplate && !isPreset; templates.Visible = isTemplate; presets.Visible = isPreset; presetType.Enabled = presetType.Visible = isPreset;
            if (isPreset)
            {
                presets.BeginUpdate(); presets.Items.Clear();
                foreach (LegacyPreset p in assets.Presets.Where(p => (selected != 8 || p.IsAutotile) && (presetType.SelectedIndex == 0 || p.Type == presetType.Text)).OrderBy(p => p.Name)) if (query == "" || p.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) presets.Items.Add(p);
                presets.EndUpdate(); return;
            }
            if (isTemplate)
            {
                templates.BeginUpdate(); templates.Items.Clear();
                foreach (Template t in (selected == 6 ? assets.AllCharacters : assets.AllItems).Where(t => selected != 11 || t.IsDoor && assets.Items.ContainsKey(t.Id) && assets.Items[t.Id].Source == t.Source && (doorFilter == null || doorFilter.Contains(t.Id))).OrderBy(t => t.Id).ThenBy(t => t.Area)) if (query.Length == 0 || (t.ToString() + " " + t.Sprite).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) templates.Items.Add(t);
                templates.EndUpdate(); return;
            }
            IEnumerable<int> ids = assets.Ids;
            if (selected == 0) ids = ArtCategories.Ids(assets, "tiles");
            else if (selected == 1) ids = ArtCategories.Ids(assets, "walls");
            else if (selected == 2) ids = ids.Where(id => id >= 1000 && id <= 65535 && assets.Rules.Lowered(id) == id);
            else if (selected == 4) ids = Canvas.Document == null ? Enumerable.Empty<int>() : Canvas.Document.Tiles.SelectMany(t => new[] { t.Sprite(0), t.Sprite(1), t.Sprite(2), t.Sprite(3) }).Where(i => i > 0).Distinct().OrderBy(i => i);
            else if (selected == 9) ids = ArtCategories.Ids(assets, "ground_overlays");
            else if (selected == 10) ids = ArtCategories.Ids(assets, "doors");
            else if (selected == 12) ids = ArtCategories.Ids(assets, "scenery");
            else if (selected == 13) ids = ArtCategories.Ids(assets, "item_art");
            else if (selected == 14) ids = ArtCategories.Ids(assets, "effects");
            else if (selected == 15) ids = assets.Rules.VariantIds.OrderBy(id => id);
            else if (selected == 16) ids = ids.Where(id => assets.HasSprite(id));
            if (selected != 16) ids = ids.Where(id => id <= 65535);
            if (query != "") ids = ids.Where(i => i.ToString().Contains(query)); library.SetSprites(ids.ToList());
        }
        private void SetTool(EditorTool tool)
        {
            Canvas.Tool = tool; Canvas.SpacePan(false);
            foreach (var pair in toolButtons) { pair.Value.BackColor = pair.Key == tool ? Theme.Active : Theme.Control; pair.Value.FlatAppearance.BorderColor = pair.Key == tool ? Theme.Gold : Theme.Border; }
        }
        private void SelectSprite(int id)
        {
            if (category.SelectedIndex == 16 || id > 65535) { Say("Image " + id + " is selected for preview. Choose a world sprite or a template to paint."); return; }
            if (!decorativeDoors.Checked && (ArtCategories.Contains("doors", id) || assets.IsDoorArtwork(id)))
            {
                int frame = assets.PreviewSprite(id);
                Template[] matches = assets.Items.Values.Where(t => t.IsDoor && (assets.PreviewSprite(t.Sprite) == frame || t.Driver == 2 && assets.PreviewSprite(t.Sprite + 1) == frame)).ToArray();
                if (matches.Length == 0) { Say("No interactive door in this area matches that picture. Choose an interactive door, or enable Decorative door pictures."); return; }
                if (matches.Length > 1) { category.SelectedIndex = 11; search.Text = ""; doorFilter = new HashSet<string>(matches.Select(t => t.Id)); RefreshLibrary(); Say("Choose the matching door template for its key, lock or destination."); return; }
                layer.SelectedIndex = 4; Canvas.BrushTemplate = matches[0].Id; Canvas.PresetBrush = null; Canvas.ConnectedBrush = null; SetTool(EditorTool.Paint); UpdateBrushLabel(); Say("Interactive door selected: " + matches[0].Id); return;
            }
            Canvas.PresetBrush = null; Canvas.ConnectedBrush = null;
            int selected = category.SelectedIndex;
            if (selected == 9 || ArtCategories.Contains("ground_overlays", id)) layer.SelectedIndex = 1;
            else if (selected == 0) layer.SelectedIndex = 0;
            else if (selected == 1 || selected == 10 || selected >= 12 && selected <= 14) layer.SelectedIndex = 2;
            else if (layer.SelectedIndex > 3) layer.SelectedIndex = 0;
            brushId.Value = id; Canvas.BrushSprite = id; SetTool(EditorTool.Paint); UpdateBrushLabel();
        }
        private void ConfigureAutotile()
        {
            using (var dialog = new AutotileForm(assets, preferences, Canvas.BrushSprite, Canvas.Layer))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    int target = dialog.Pattern != null ? dialog.Pattern.Affect : dialog.Brush.Layer;
                    int sprite = dialog.Pattern != null ? dialog.Pattern.Sprites[0] : dialog.Brush.Pieces[0];
                    layer.SelectedIndex = target; brushId.Value = sprite; Canvas.BrushSprite = sprite; Canvas.PresetBrush = dialog.Pattern; Canvas.ConnectedBrush = dialog.Brush;
                    Canvas.AutoWalls = true; foreach (CheckBox check in FindAllControls(this).OfType<CheckBox>()) if ((string)check.Tag == "AutoWalls") check.Checked = true;
                    SetTool(EditorTool.Paint); UpdateBrushLabel(); SavePreferences(); Say(dialog.Pattern != null ? "Autotile pattern ready. Paint or rectangle to place the full variation set." : "Connected set ready. Paint or rectangle; matching neighbors reconnect in the same undo step.");
                }
        }
        private void SavePreferences()
        {
            preferences.VisibleFlags = Canvas.FlagOverlay.Visible.Where(flag => flag != "MF_NEUTRAL").ToArray();
            try { preferences.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "editor-settings.json")); } catch (IOException) { Say("View preferences are active for this session; the app folder is read-only."); } catch (UnauthorizedAccessException) { Say("View preferences are active for this session; the app folder is read-only."); }
        }
        private void CreateTemplate()
        {
            if (assets.ZonesDirectory == null) { Say("Choose a zones folder through Sources first."); return; }
            using (var dialog = new TemplateForm(assets, Canvas.BrushSprite))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    assets.LoadZones(assets.ZonesDirectory); ReloadTemplates(); category.SelectedIndex = dialog.CreatedCharacter ? 6 : 5; search.Text = dialog.CreatedId; RefreshLibrary();
                    if (dialog.CreatedArea == assets.ActiveArea || dialog.CreatedArea == "generic") { layer.SelectedIndex = dialog.CreatedCharacter ? 5 : 4; Canvas.BrushTemplate = dialog.CreatedId; SetTool(EditorTool.Paint); UpdateBrushLabel(); }
                    Canvas.NotifyChanged(); Say("Saved template " + dialog.CreatedId + " in area " + dialog.CreatedArea + ".");
                }
        }
        private void SaveZoneFiles()
        {
            try
            {
                if (Canvas.Document == null) { Say("Open a map first."); return; }
                ZoneSavePlan plan = ZoneSavePlan.Create(assets, zoneBaseline, Canvas.Document);
                using (var dialog = new Form { Text = "Map and linked zone changes", ClientSize = new Size(790, 480), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Font })
                {
                    var summary = Theme.TextBox(760); summary.Multiline = true; summary.ReadOnly = true; summary.ScrollBars = ScrollBars.Both; summary.Dock = DockStyle.Fill; summary.Text = string.Join(Environment.NewLine + Environment.NewLine, plan.Summary);
                    var commit = Theme.Button("Save map + zone arguments", 245); commit.Dock = DockStyle.Bottom;
                    commit.Click += (s, e) => { try { plan.Commit(); Canvas.Document.AcceptSavedPath(zoneBaseline.SourcePath); zoneBaseline = MapDocument.Load(zoneBaseline.SourcePath); assets.LoadZones(assets.ZonesDirectory); ReloadTemplates(); Canvas.NotifyChanged(); dialog.DialogResult = DialogResult.OK; dialog.Close(); Say("Saved and backed up " + plan.Files.Count + " zone files."); } catch (Exception error) { MessageBox.Show(dialog, error.Message, "Could not save zone files", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
                    dialog.Controls.Add(summary); dialog.Controls.Add(commit); dialog.ShowDialog(this);
                }
            }
            catch (Exception error) { Error("Could not prepare zone save", error); }
        }
        private void ExportSnapshot()
        {
            if (Canvas.Document == null) { Say("Open a map first."); return; }
            using (var quality = new PngExportForm(true))
                if (quality.ShowDialog(this) == DialogResult.OK)
                    using (var dialog = new SaveFileDialog { Filter = "PNG overview (*.png)|*.png", DefaultExt = "png", AddExtension = true, FileName = Path.GetFileNameWithoutExtension(Canvas.Document.Name) + "-overview.png" })
                        if (dialog.ShowDialog(this) == DialogResult.OK)
                            try { Cursor = Cursors.WaitCursor; MapSnapshot.Export(Canvas.Document, assets, dialog.FileName, quality.OverviewWidth, quality.Smooth, quality.FinestArtwork, quality.NativeScale); Say("Saved stitched " + (quality.NativeScale > 0 ? "native " + quality.NativeScale + "×" : quality.OverviewWidth + "-pixel") + " map overview: " + dialog.FileName); }
                            catch (Exception error) { Error("Could not export overview", error); }
                            finally { Cursor = Cursors.Default; }
        }
        private void UpdateBrushLabel()
        {
            brushLabel.Text = Canvas.PresetBrush != null ? Canvas.PresetBrush.Name + " · " + Canvas.PresetBrush.Type : Canvas.ConnectedBrush != null ? "Connected set · " + LayerNames[Canvas.Layer] : LayerNames[Canvas.Layer] + "  ·  " + (Canvas.Layer < 4 ? Canvas.BrushSprite.ToString() : Canvas.Layer == 6 ? Canvas.BrushFlags.Count + " flags" : Canvas.Layer == 7 ? "all layers and flags" : Canvas.BrushTemplate == "" ? "empty" : Canvas.BrushTemplate);
            tips.SetToolTip(brushLabel, brushLabel.Text);
        }
        private void OpenSelectedMap() { if (maps.SelectedItem != null && assets.ZonesDirectory != null) OpenMap(Path.Combine(assets.ZonesDirectory, maps.SelectedItem.ToString())); }
        private void OpenFile()
        {
            using (var f = new OpenFileDialog { Filter = "Astonia maps (*.map)|*.map|All files (*.*)|*.*", InitialDirectory = assets.ZonesDirectory ?? AppDomain.CurrentDomain.BaseDirectory }) if (f.ShowDialog(this) == DialogResult.OK) OpenMap(f.FileName);
        }
        public void OpenMap(string path)
        {
            if (!ConfirmDiscard()) return;
            try
            {
                Cursor = Cursors.WaitCursor; MapDocument document = MapDocument.Load(path);
                if (document.AreaContext == null || (assets.ZonesDirectory != null && Path.GetFullPath(path).StartsWith(assets.ZonesDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) document.AreaContext = assets.AreaForMap(path);
                zoneBaseline = assets.ZonesDirectory != null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(assets.ZonesDirectory).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ? MapDocument.Load(path) : null;
                Canvas.SetDocument(document); ReloadTemplates(); LoadInspector(); RefreshLibrary(); mini.RefreshMap(); UpdateDocumentState();
                Say(document.Name + " · area " + assets.ActiveArea + " · " + assets.Summary);
            }
            catch (Exception ex) { Error("Could not open map", ex); }
            finally { Cursor = Cursors.Default; }
        }
        private void NewMap()
        {
            if (!ConfirmDiscard()) return;
            zoneBaseline = null; Canvas.SetDocument(new MapDocument { AreaContext = assets.ActiveArea }); LoadInspector(); mini.RefreshMap(); RefreshLibrary(); UpdateDocumentState();
            Say("New 256 x 256 map. Choose a sprite and Paint (B), Rectangle (R), or Fill (F).");
        }
        private bool SaveMap(bool forceDialog)
        {
            if (Canvas.Document == null) { Say("Open or create a map first."); return false; }
            Canvas.Document.EndEdit(); string path = Canvas.Document.SavePath;
            if (forceDialog || path == null)
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Maps");
                Directory.CreateDirectory(folder);
                using (var f = new SaveFileDialog { Filter = "Astonia map (*.map)|*.map", DefaultExt = "map", AddExtension = true, FileName = Canvas.Document.Name, InitialDirectory = path == null ? folder : Path.GetDirectoryName(path) })
                    if (f.ShowDialog(this) == DialogResult.OK) path = f.FileName; else return false;
            }
            try { Canvas.Document.Save(path); UpdateDocumentState(); Say("Saved and verified: " + path); return true; }
            catch (Exception ex) { Error("Could not save map", ex); return false; }
        }
        private bool ConfirmDiscard()
        {
            if (Canvas.Document == null) return true;
            Canvas.Document.EndEdit(); if (!Canvas.Document.Dirty) return true;
            DialogResult r = MessageBox.Show(this, "Save changes to " + Canvas.Document.Name + " before continuing?", "Unsaved map", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            return r == DialogResult.No || (r == DialogResult.Yes && SaveMap(false));
        }
        private void LoadInspector()
        {
            bool has = Canvas.Document != null && Canvas.Document.Contains(Canvas.Selected.X, Canvas.Selected.Y);
            selectionLabel.Text = has ? "TILE  " + Canvas.Selected.X + " , " + Canvas.Selected.Y : "Select a tile to inspect";
            foreach (Control c in new Control[] { g1, g2, f1, f2, item, npc, tileFlags }) c.Enabled = has;
            Tile tile = has ? Canvas.Document.At(Canvas.Selected.X, Canvas.Selected.Y) : new Tile();
            Rectangle selectedArea = Canvas.Selection; inspectorBase = tile.Clone(); apply.Text = selectedArea.IsEmpty ? "Apply to selected tile" : "Apply edits to " + (selectedArea.Width * selectedArea.Height) + " tiles";
            g1.Value = tile.Sprite(0); g2.Value = tile.Sprite(1); f1.Value = tile.Sprite(2); f2.Value = tile.Sprite(3); item.Text = tile.Item; npc.Text = tile.Character;
            SetChecked(tileFlags, tile.Flags);
        }
        private static HashSet<string> GetChecked(CheckedListBox box) { return new HashSet<string>(box.CheckedItems.Cast<FlagInfo>().Select(f => f.Name), StringComparer.OrdinalIgnoreCase); }
        private static void SetChecked(CheckedListBox box, HashSet<string> values)
        {
            // Keep unknown flags available instead of silently dropping them.
            foreach (string flag in values) if (!box.Items.Cast<FlagInfo>().Any(f => f.Name.Equals(flag, StringComparison.OrdinalIgnoreCase))) box.Items.Add(EditorFlags.Find(flag));
            for (int i = 0; i < box.Items.Count; i++) box.SetItemChecked(i, values.Contains(((FlagInfo)box.Items[i]).Name));
        }
        private void ApplyInspector()
        {
            if (Canvas.Document == null || !Canvas.Document.Contains(Canvas.Selected.X, Canvas.Selected.Y)) { Say("Click a map tile with Inspect (I) first."); return; }
            string it = item.Text.Trim(), ch = npc.Text.Trim();
            if (!ValidTemplateId(it) || !ValidTemplateId(ch)) { Say("Template IDs may contain letters, numbers, and underscores. Leave the field empty to clear it."); return; }
            if ((it != "" && !assets.Items.ContainsKey(it)) || (ch != "" && !assets.Characters.ContainsKey(ch)))
                if (MessageBox.Show(this, "A template is not in the currently loaded zone files. Keep this reference?", "Unknown template", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int[] sprites = { (int)g1.Value, (int)g2.Value, (int)f1.Value, (int)f2.Value }; HashSet<string> flags = GetChecked(tileFlags); Tile original = inspectorBase ?? Canvas.Document.At(Canvas.Selected.X, Canvas.Selected.Y).Clone();
            Rectangle selectedArea = Canvas.Selection; bool multiple = !selectedArea.IsEmpty; Rectangle target = multiple ? selectedArea : new Rectangle(Canvas.Selected.X, Canvas.Selected.Y, 1, 1);
            Canvas.Document.BeginEdit();
            for (int yy = target.Top; yy < target.Bottom; yy++) for (int xx = target.Left; xx < target.Right; xx++) Canvas.Document.Edit(xx, yy, t =>
            {
                string previousItem = t.Item;
                for (int index = 0; index < 4; index++) if (!multiple || sprites[index] != original.Sprite(index)) t.SetSprite(index, sprites[index]);
                if (!multiple || it != original.Item) t.Item = it;
                if (!multiple || ch != original.Character) t.Character = ch;
                if (!multiple || !flags.SetEquals(original.Flags)) t.Flags = new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);
                if (t.Item != previousItem) assets.PrepareDoorTile(t, t.Item);
            });
            if (Canvas.Document.EndEdit()) Canvas.NotifyChanged(); Say("Tile properties applied.");
        }
        private static bool ValidTemplateId(string value) { return value.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9_]+$"); }
        private void UpdateDocumentState()
        {
            MapDocument d = Canvas.Document; undo.Enabled = d != null && d.CanUndo; redo.Enabled = d != null && d.CanRedo; save.Enabled = d != null;
            fileLabel.Text = d == null ? "No map open" : (d.Dirty ? "* " : "") + d.Name + (d.SavePath == null ? "  ·  source copy" : "  ·  working copy");
            Text = (d == null ? "" : (d.Dirty ? "* " : "") + d.Name + " — ") + "Astonia World Editor 0.3.2";
            save.Text = d != null && d.SavePath != null ? "Save" : "Save copy";
        }
        private void GoTo()
        {
            string[] parts = goTo.Text.Split(','); int x, y;
            if (parts.Length != 2 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y) || x < 0 || y < 0 || x > 255 || y > 255) { Say("Enter coordinates as x,y, between 0 and 255."); return; }
            Canvas.CenterOn(x, y); Canvas.Selected = new Point(x, y); LoadInspector();
        }
        private void ExportPng()
        {
            if (Canvas.Document == null) { Say("Open a map first."); return; }
            using (var quality = new PngExportForm(false))
                if (quality.ShowDialog(this) == DialogResult.OK)
                    using (var f = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = Path.GetFileNameWithoutExtension(Canvas.Document.Name) + "-view.png", DefaultExt = "png", AddExtension = true })
                        if (f.ShowDialog(this) == DialogResult.OK) try { Cursor = Cursors.WaitCursor; Canvas.ExportPng(f.FileName, quality.Multiplier, quality.Smooth, quality.FinestArtwork); Say("Exported " + Canvas.Width * quality.Multiplier + " × " + Canvas.Height * quality.Multiplier + " PNG: " + f.FileName); } catch (Exception ex) { Error("Could not export PNG", ex); } finally { Cursor = Cursors.Default; }
        }
        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S) { SaveMap(e.Shift); e.SuppressKeyPress = true; return; }
            if (e.Control && e.KeyCode == Keys.O) { OpenFile(); e.SuppressKeyPress = true; return; }
            if (e.Control && e.KeyCode == Keys.N) { NewMap(); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.F1) { ShowHelp(); e.SuppressKeyPress = true; return; }
            if (TextInputFocused()) return;
            if (e.Control)
            {
                if (e.KeyCode == Keys.Z) { if (Canvas.Document != null) { if (e.Shift) Canvas.Document.Redo(); else Canvas.Document.Undo(); Canvas.NotifyChanged(); } }
                else if (e.KeyCode == Keys.Y) { if (Canvas.Document != null) { Canvas.Document.Redo(); Canvas.NotifyChanged(); } }
                else if (e.KeyCode == Keys.C) Canvas.CopySelection(); else if (e.KeyCode == Keys.V) Canvas.BeginPaste(); else return;
                e.SuppressKeyPress = true; return;
            }
            EditorTool tool;
            switch (e.KeyCode)
            {
                case Keys.I: tool = EditorTool.Inspect; break; case Keys.B: tool = EditorTool.Paint; break; case Keys.R: tool = EditorTool.Rectangle; break;
                case Keys.F: tool = EditorTool.Fill; break; case Keys.E: tool = EditorTool.Erase; break; case Keys.P: tool = EditorTool.Pick; break;
                case Keys.S: tool = EditorTool.Select; break; case Keys.H: tool = EditorTool.Pan; break;
                case Keys.Space: Canvas.SpacePan(true); e.SuppressKeyPress = true; return;
                case Keys.Escape: Canvas.Cancel(); e.SuppressKeyPress = true; return;
                case Keys.Delete: Canvas.ClearSelectionLayer(); e.SuppressKeyPress = true; return;
                case Keys.F8: foreach (CheckBox box in FindAllControls(this).OfType<CheckBox>()) if ((string)box.Tag == "F8") box.Checked = !box.Checked; e.SuppressKeyPress = true; return;
                default: return;
            }
            SetTool(tool); e.SuppressKeyPress = true;
        }
        private bool TextInputFocused()
        {
            Control focused = ActiveControl; while (focused is ContainerControl && ((ContainerControl)focused).ActiveControl != null) focused = ((ContainerControl)focused).ActiveControl;
            return FindAllControls(this).Any(c => c.Focused && (c is TextBoxBase || c is NumericUpDown || (c is ComboBox && ((ComboBox)c).DropDownStyle != ComboBoxStyle.DropDownList)));
        }
        private static IEnumerable<Control> FindAllControls(Control root) { foreach (Control c in root.Controls) { yield return c; foreach (Control child in FindAllControls(c)) yield return child; } }
        private void ShowHelp()
        {
            MessageBox.Show(this, "ASTONIA WORLD EDITOR  ·  0.3.2\n\nSources scans every res/zones subfolder. Files shows all resources, archive entries, definitions and load warnings. Maps open as source copies; saved working files keep timestamped backups.\n\nPresets loads original combined and weighted brushes. Autotiles loads original ground patterns and line-wall sequences. Choose a brush, then Paint or Rectangle.\n\nNPCs shows actual template artwork. Low walls / F8 substitutes short sprites from the client's metadata. Flags shows letter identifiers beside their canonical names.\n\nSight follows the hovered tile and outlines map (S) / temporary or item (TS) sight blockers. The number sets the range. The dashed line targets the selected tile. This previews server visibility geometry with placed item flags; live door states, darkness and character effects require a running game.\n\nI Inspect · B Paint · R Rectangle · F Fill\nE Erase · P Pick · S Select · H Pan\nRight drag / Space: pan · Wheel: zoom\nCtrl+Z/Y: undo/redo · Ctrl+C/V: area copy/paste\nDelete: clear selected area's active layer · Esc: cancel\nCtrl+O/N/S: open/new/save · Ctrl+Shift+S: save as\n\nTemplates from every area are browsable; painting uses the current area's definitions plus generic templates. Invalid presets stay visible with load warnings. PNG view exports the current camera.\n\nBased on the Astonia 3 engine by Daniel Brockhaus — brockhaus.org.", "Editor help", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void Say(string message) { status.Text = message; tips.SetToolTip(status, message); }
        private void Error(string title, Exception ex) { MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error); Say(title + ": " + ex.Message); }
        protected override void Dispose(bool disposing) { if (disposing) { searchTimer.Dispose(); tips.Dispose(); assets.Dispose(); } base.Dispose(disposing); }
    }
}

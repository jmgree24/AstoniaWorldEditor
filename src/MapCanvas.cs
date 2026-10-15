using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace Astonia.Editor
{
    public enum EditorTool { Inspect, Paint, Rectangle, Fill, Erase, Pick, Select, Pan }
    public sealed class MapCanvas : Control
    {
        public MapDocument Document;
        public AssetCatalog Assets;
        public EditorTool Tool = EditorTool.Inspect;
        public int Layer, BrushSize = 1;
        public int BrushSprite { get; set; }
        public string BrushTemplate = "";
        public LegacyPreset PresetBrush;
        public ConnectedTiles ConnectedBrush;
        public Tile WholeTileBrush;
        public readonly FlagOverlay FlagOverlay = new FlagOverlay();
        public HashSet<string> BrushFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string FlagOperation = "Add";
        public bool ShowGrid, ShowFlags, ShowForeground = true, ShowMarkers = true, ShowNpcs = true, LowerWalls, ShowSight, ShowCollision, ShowSpikes = true, AutoWalls = true, EnhancedSprites = true;
        public bool DecorativeDoors;
        public bool ShowFlagOverlays = true;
        public bool SmoothSprites;
        public int PreferredGraphicsScale;
        public Point RenderOrigin;
        public int SightRange = 20;
        public float Zoom = 2;
        public Point Selected { get; set; }
        public Point Hover = new Point(-1, -1);
        public Rectangle Selection = Rectangle.Empty;
        public event Action Changed, TileSelected, ViewChanged, BrushPicked;
        public event Action<string> Status;
        public event Action<int> DoorPictureRequested;
        private float panX, panY;
        private Point mouseStart, tileStart, previousTile;
        private float oldPanX, oldPanY;
        private bool dragging, panning, space, pasteMode;
        private Tile[,] clipboard;
        private readonly Random brushRandom = new Random();
        private readonly HashSet<int> paintedInStroke = new HashSet<int>();
        private SightPreview sight;
        private int sightRevision = -1;
        private string sightArea;
        private float spriteOpacity = 1;
        private Point[] Diamond(float cx, float cy)
        {
            return new[] { new Point((int)cx, (int)(cy - 10 * Zoom)), new Point((int)(cx + 20 * Zoom), (int)cy), new Point((int)cx, (int)(cy + 10 * Zoom)), new Point((int)(cx - 20 * Zoom), (int)cy) };
        }
        public MapCanvas()
        {
            BrushSprite = 12000; Selected = new Point(-1, -1);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Canvas; TabStop = true; Cursor = Cursors.Cross;
        }
        public void SetDocument(MapDocument document)
        {
            Document = document; Selected = new Point(128, 128); Selection = Rectangle.Empty; pasteMode = false;
            PresetBrush = null; ConnectedBrush = null; WholeTileBrush = null; sight = null;
            CenterOn(128, 128); Invalidate();
        }
        public Point ScreenToTile(Point p)
        {
            float wx = (p.X - panX) / Zoom, wy = (p.Y - panY) / Zoom;
            return new Point((int)Math.Floor((wx / 20 + wy / 10) / 2 + 0.5f), (int)Math.Floor((wy / 10 - wx / 20) / 2 + 0.5f));
        }
        public PointF TileToScreen(int x, int y) { return new PointF(panX + (x - y) * 20 * Zoom, panY + (x + y) * 10 * Zoom); }
        public void CenterOn(int x, int y)
        {
            panX = Width / 2f - (x - y) * 20 * Zoom; panY = Height / 2f - (x + y) * 10 * Zoom;
            Invalidate(); if (ViewChanged != null) ViewChanged();
        }
        public void ZoomAt(float value, Point anchor)
        {
            value = Math.Max(0.05f, Math.Min(8, value));
            float wx = (anchor.X - panX) / Zoom, wy = (anchor.Y - panY) / Zoom;
            Zoom = value; panX = anchor.X - wx * Zoom; panY = anchor.Y - wy * Zoom;
            Invalidate(); if (ViewChanged != null) ViewChanged();
        }
        public void Fit() { Zoom = Math.Max(0.05f, Math.Min((Width - 40) / 10240f, (Height - 40) / 5120f)); CenterOn(127, 127); }
        public void SpacePan(bool value) { space = value; Cursor = value || Tool == EditorTool.Pan ? Cursors.Hand : Cursors.Cross; }
        protected override void OnMouseWheel(MouseEventArgs e) { ZoomAt(Zoom * (e.Delta > 0 ? 1.2f : 1 / 1.2f), e.Location); base.OnMouseWheel(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus(); if (Document == null) return;
            Capture = true; mouseStart = e.Location; tileStart = previousTile = ScreenToTile(e.Location);
            oldPanX = panX; oldPanY = panY;
            panning = e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle || Tool == EditorTool.Pan && (ModifierKeys & Keys.Shift) == 0 || space;
            if (panning) { dragging = true; Cursor = Cursors.Hand; return; }
            if (e.Button != MouseButtons.Left || !Document.Contains(tileStart.X, tileStart.Y)) { Capture = false; return; }
            Selection = Rectangle.Empty; Selected = tileStart; if (TileSelected != null) TileSelected();
            if ((ModifierKeys & Keys.Shift) != 0) { PickWholeTile(tileStart); Capture = false; return; }
            if (pasteMode) { PasteAt(tileStart); pasteMode = false; return; }
            if (Tool == EditorTool.Pick) { Pick(tileStart); return; }
            if (PresetBrush == null && Layer < 4 && !DecorativeDoors && Assets != null && (ArtCategories.Contains("doors", BrushSprite) || Assets.IsDoorArtwork(BrushSprite)) && (Tool == EditorTool.Paint || Tool == EditorTool.Rectangle || Tool == EditorTool.Fill))
            { if (DoorPictureRequested != null) DoorPictureRequested(BrushSprite); if (Layer < 4) { Capture = false; return; } }
            if (Layer == 7 && WholeTileBrush == null && Tool != EditorTool.Erase) { Say("Shift-click a tile to load the whole-tile brush first."); Capture = false; return; }
            if (PresetBrush == null && (Layer == 4 || Layer == 5) && BrushTemplate == "" && (Tool == EditorTool.Paint || Tool == EditorTool.Rectangle || Tool == EditorTool.Fill))
            { Say("Choose an item/NPC template for the active area before painting. Use Erase to clear this layer."); Capture = false; return; }
            if (Tool == EditorTool.Fill)
            {
                if (PresetBrush != null) { Say("Use Paint or Rectangle with presets and autotiles."); Capture = false; return; }
                int count = Document.Flood(tileStart.X, tileStart.Y, LayerKey, ApplyBrush);
                NotifyChanged(); Say("Filled " + count.ToString("N0") + " tiles on " + MainForm.LayerNames[Layer] + "."); return;
            }
            if (Tool == EditorTool.Inspect) { Invalidate(); return; }
            dragging = true;
            paintedInStroke.Clear();
            if (Tool == EditorTool.Select) Selection = new Rectangle(tileStart.X, tileStart.Y, 1, 1);
            else { Document.BeginEdit(); if (Tool == EditorTool.Paint || Tool == EditorTool.Erase) Stamp(tileStart); }
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); Hover = ScreenToTile(e.Location);
            if (dragging && panning)
            {
                panX = oldPanX + e.X - mouseStart.X; panY = oldPanY + e.Y - mouseStart.Y;
                if (ViewChanged != null) ViewChanged();
            }
            else if (dragging && Document != null)
            {
                if (Tool == EditorTool.Select) Selection = TileRect(tileStart, Hover);
                else if (Tool == EditorTool.Paint || Tool == EditorTool.Erase) { PaintLine(previousTile, Hover); previousTile = Hover; }
            }
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging && !panning && Document != null)
            {
                if (Tool == EditorTool.Rectangle)
                {
                    Rectangle rect = TileRect(tileStart, ScreenToTile(e.Location));
                    for (int y = rect.Top; y < rect.Bottom; y++) for (int x = rect.Left; x < rect.Right; x++) PaintTile(x, y);
                }
                if (Document.EndEdit()) NotifyChanged();
            }
            dragging = panning = false; Capture = false; SpacePan(space); Invalidate();
            if (Tool == EditorTool.Select && TileSelected != null) TileSelected();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && dragging) { dragging = panning = false; if (Document != null && Document.EndEdit()) NotifyChanged(); Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (!dragging) { Hover = new Point(-1, -1); Invalidate(); } }
        public void Cancel() { pasteMode = false; Selection = Rectangle.Empty; dragging = panning = false; Capture = false; if (Document != null && Document.EndEdit()) NotifyChanged(); Invalidate(); }
        private Rectangle TileRect(Point a, Point b)
        {
            int left = Math.Max(0, Math.Min(a.X, b.X)), top = Math.Max(0, Math.Min(a.Y, b.Y));
            int right = Math.Min(255, Math.Max(a.X, b.X)), bottom = Math.Min(255, Math.Max(a.Y, b.Y));
            return Rectangle.FromLTRB(left, top, Math.Max(left, right + 1), Math.Max(top, bottom + 1));
        }
        private string LayerKey(Tile tile)
        {
            if (Layer < 4) return tile.Sprite(Layer).ToString();
            if (Layer == 4) return tile.Item; if (Layer == 5) return tile.Character;
            if (Layer == 7) return tile.Ground + "/" + tile.Foreground + "/" + tile.Item + "/" + tile.Character + "/" + string.Join(",", tile.Flags.OrderBy(f => f));
            return string.Join(",", tile.Flags.OrderBy(f => f));
        }
        private void ApplyBrush(Tile tile)
        {
            bool erase = Tool == EditorTool.Erase;
            if (Layer < 4) tile.SetSprite(Layer, erase ? 0 : BrushSprite);
            else if (Layer == 4) tile.Item = erase ? "" : BrushTemplate;
            else if (Layer == 5) tile.Character = erase ? "" : BrushTemplate;
            else if (Layer == 7)
            {
                Tile source = erase ? new Tile() : WholeTileBrush;
                if (source != null) CopyTile(source, tile);
            }
            else if (erase) tile.Flags.Clear();
            else if (FlagOperation == "Replace") tile.Flags = new HashSet<string>(BrushFlags, StringComparer.OrdinalIgnoreCase);
            else if (FlagOperation == "Remove") tile.Flags.ExceptWith(BrushFlags);
            else tile.Flags.UnionWith(BrushFlags);
            if (!erase && Layer == 4 && Assets != null) Assets.PrepareDoorTile(tile, tile.Item);
        }
        private void Stamp(Point p)
        {
            for (int y = 0; y < BrushSize; y++) for (int x = 0; x < BrushSize; x++) PaintTile(p.X + x, p.Y + y);
        }
        private void PaintTile(int x, int y)
        {
            if (!Document.Contains(x, y)) return;
            ConnectedTiles connections = ConnectedBrush ?? new ConnectedTiles(); bool previousWall = Layer < 4 && connections.Member(Document.At(x, y).Sprite(Layer));
            if (PresetBrush != null && Tool != EditorTool.Erase)
            {
                if (!paintedInStroke.Add(x + y * 256)) return;
                Document.Edit(x, y, t => PresetBrush.Apply(Document, x, y, t, brushRandom));
            }
            else Document.Edit(x, y, ApplyBrush);
            if (AutoWalls && Layer < 4 && PresetBrush == null && (previousWall || connections.Member(Document.At(x, y).Sprite(Layer)))) connections.Reconnect(Document, x, y, Layer);
            sight = null;
        }
        private void PaintLine(Point a, Point b)
        {
            int steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            // A captured pointer can travel far outside the map; bound work per event.
            steps = Math.Min(1024, steps);
            for (int i = 0; i <= steps; i++) Stamp(new Point(a.X + (int)Math.Round((b.X - a.X) * (steps == 0 ? 0 : (double)i / steps)), a.Y + (int)Math.Round((b.Y - a.Y) * (steps == 0 ? 0 : (double)i / steps))));
        }
        private void Pick(Point p)
        {
            PresetBrush = null;
            Tile t = Document.At(p.X, p.Y);
            if (Layer < 4) BrushSprite = t.Sprite(Layer);
            else if (Layer < 6) BrushTemplate = Layer == 4 ? t.Item : t.Character;
            else if (Layer == 7) WholeTileBrush = t.Clone();
            else BrushFlags = new HashSet<string>(t.Flags, StringComparer.OrdinalIgnoreCase);
            if (BrushPicked != null) BrushPicked(); Say("Picked " + MainForm.LayerNames[Layer] + " from " + p.X + "," + p.Y + ".");
        }
        public void PickWholeTile(Point p)
        {
            WholeTileBrush = Document.At(p.X, p.Y).Clone(); PresetBrush = null; ConnectedBrush = null; Layer = 7; Tool = EditorTool.Paint;
            if (BrushPicked != null) BrushPicked(); Say("Whole tile picked. Paint to stamp all layers and flags."); Invalidate();
        }
        private static void CopyTile(Tile source, Tile target)
        { target.Ground = source.Ground; target.Foreground = source.Foreground; target.Item = source.Item; target.Character = source.Character; target.Flags = new HashSet<string>(source.Flags, StringComparer.OrdinalIgnoreCase); target.Extra = new List<string>(source.Extra); }
        public void CopySelection()
        {
            if (Document == null || Selection.IsEmpty) { Say("Use Select (S) and drag an area first."); return; }
            clipboard = new Tile[Selection.Width, Selection.Height];
            for (int y = 0; y < Selection.Height; y++) for (int x = 0; x < Selection.Width; x++) clipboard[x, y] = Document.At(Selection.X + x, Selection.Y + y).Clone();
            Say("Copied " + Selection.Width + " x " + Selection.Height + " tiles, including all layers and flags.");
        }
        public void BeginPaste() { if (clipboard == null) { Say("Copy a selected area first."); return; } pasteMode = true; Say("Click the upper-left tile to paste all layers. Esc cancels."); Invalidate(); }
        private void PasteAt(Point point)
        {
            Document.BeginEdit(); int clipped = 0;
            for (int y = 0; y < clipboard.GetLength(1); y++) for (int x = 0; x < clipboard.GetLength(0); x++)
            {
                if (!Document.Contains(point.X + x, point.Y + y)) { clipped++; continue; }
                Tile t = clipboard[x, y]; Document.Edit(point.X + x, point.Y + y, tile => { tile.Ground = t.Ground; tile.Foreground = t.Foreground; tile.Item = t.Item; tile.Character = t.Character; tile.Flags = new HashSet<string>(t.Flags, StringComparer.OrdinalIgnoreCase); tile.Extra = new List<string>(t.Extra); });
            }
            if (Document.EndEdit()) NotifyChanged(); Say("Area pasted." + (clipped > 0 ? " " + clipped + " tiles outside map bounds were skipped." : ""));
        }
        public void ClearSelectionLayer()
        {
            if (Document == null || Selection.IsEmpty) return;
            EditorTool previous = Tool; Tool = EditorTool.Erase; Document.BeginEdit();
            for (int y = Selection.Top; y < Selection.Bottom; y++) for (int x = Selection.Left; x < Selection.Right; x++) Document.Edit(x, y, ApplyBrush);
            Tool = previous; if (Document.EndEdit()) NotifyChanged();
        }
        public void NotifyChanged() { sight = null; Invalidate(); if (Changed != null) Changed(); }
        private void Say(string value) { if (Status != null) Status(value); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics;
            if (Document == null)
            {
                using (var font = new Font("Segoe UI", 23)) using (var brush = new SolidBrush(Theme.Text)) g.DrawString("A world of your own.", font, brush, new PointF(38, Height / 2f - 65));
                using (var brush = new SolidBrush(Theme.Muted)) g.DrawString("Open a map or create a new 256 x 256 world to begin.", Font, brush, 40, Height / 2f - 16);
                return;
            }
            Render(g, false);
        }
        private void Render(Graphics g, bool export)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.SmoothingMode = SmoothingMode.None;
            Point observer = Document.Contains(Hover.X, Hover.Y) ? Hover : Selected;
            if (ShowSight && Document.Contains(observer.X, observer.Y) && (sight == null || sight.Origin != observer || sight.Range != SightRange || sightRevision != Document.Revision || sightArea != (Assets == null ? "" : Assets.ActiveArea)))
            {
                sight = new SightPreview(Document, Assets, observer, SightRange); sightRevision = Document.Revision; sightArea = Assets == null ? "" : Assets.ActiveArea;
            }
            int marginX = (int)Math.Ceiling(300 * Zoom), marginY = (int)Math.Ceiling(500 * Zoom);
            var corners = new[] { ScreenToTile(new Point(RenderOrigin.X - marginX, RenderOrigin.Y - marginY)), ScreenToTile(new Point(RenderOrigin.X + Width + marginX, RenderOrigin.Y - marginY)), ScreenToTile(new Point(RenderOrigin.X - marginX, RenderOrigin.Y + Height + marginY)), ScreenToTile(new Point(RenderOrigin.X + Width + marginX, RenderOrigin.Y + Height + marginY)) };
            int minX = Math.Max(0, corners.Min(p => p.X)), maxX = Math.Min(255, corners.Max(p => p.X));
            int minY = Math.Max(0, corners.Min(p => p.Y)), maxY = Math.Min(255, corners.Max(p => p.Y));
            // Draw grounds before depth-sorted objects, as in the game's isometric view.
            for (int pass = 0; pass < 3; pass++) for (int sum = minX + minY; sum <= maxX + maxY; sum++)
                for (int x = Math.Max(minX, sum - maxY); x <= Math.Min(maxX, sum - minY); x++)
                {
                    int y = sum - x; Tile t = Document.At(x, y); PointF p = TileToScreen(x, y);
                    if (pass < 2)
                    {
                        if (pass == 0 && t.Ground == 0 && Zoom >= 0.5f && !export) using (var pen = new Pen(Color.FromArgb(33, 37, 43))) g.DrawPolygon(pen, Diamond(p.X, p.Y));
                        DrawMapSprite(g, t.Sprite(pass), p, false, x, y);
                        if (pass == 1 && ShowGrid && Zoom >= 0.4f) using (var pen = new Pen(Color.Black)) g.DrawPolygon(pen, Diamond(p.X, p.Y));
                        if (pass == 1 && ShowCollision && (t.Flags.Contains("MF_MOVEBLOCK") || t.Flags.Contains("MF_TMOVEBLOCK") || (Assets != null && Assets.IsMoveBlocked(t))))
                            using (var brush = new SolidBrush(Color.FromArgb(76, 228, 94, 85))) g.FillPolygon(brush, Diamond(p.X, p.Y));
                    }
                    else
                    {
                        if (ShowSight && sight != null && !sight.Visible(x, y)) continue;
                        int elevation = Assets == null ? 0 : Assets.Rules.SurfaceHeight(Assets.PreviewSprite(t.Sprite(2), false, x, y));
                        if (ShowForeground) { DrawMapSprite(g, t.Sprite(2), p, true, x, y); DrawMapSprite(g, t.Sprite(3), new PointF(p.X, p.Y - elevation * Zoom), true, x, y); }
                        Template item;
                        if (Assets != null && Assets.Items.TryGetValue(t.Item, out item)) DrawMapSprite(g, item.Sprite, new PointF(p.X, p.Y - elevation * Zoom), true, x, y);
                        if (ShowNpcs && Assets != null && t.Character != "")
                        {
                            Template npc; if (Assets.Characters.TryGetValue(t.Character, out npc))
                            {
                                CharacterArt character = Assets.Rules.Npc(npc.Sprite, npc.Direction); int frame = character.Frame;
                                if (!Assets.HasSprite(frame)) frame = 100000 + (frame - 100000) / 1000 * 1000 + npc.Direction;
                                DrawArt(g, Assets.Art(frame, PreferredGraphicsScale > 0 ? PreferredGraphicsScale : (int)Math.Ceiling(Zoom), character.Effects), p, character.Scale);
                            }
                        }
                        if (ShowMarkers && Zoom >= 0.3f)
                        {
                            if (t.Item != "") Marker(g, p.X - 5, p.Y, Theme.Gold, "I");
                            if (t.Character != "") Marker(g, p.X + 5, p.Y - 8, Color.FromArgb(114, 174, 211), "N");
                        }
                    }
                }
            if (ShowSight && sight != null)
            {
                for (int sum = minX + minY; sum <= maxX + maxY; sum++) for (int x = Math.Max(minX, sum - maxY); x <= Math.Min(maxX, sum - minY); x++)
                {
                    int y = sum - x; PointF p = TileToScreen(x, y); Tile tile = Document.At(x, y);
                    if (!sight.Visible(x, y)) using (var shade = new SolidBrush(Color.FromArgb(165, 6, 8, 12))) g.FillPolygon(shade, Diamond(p.X, p.Y));
                    else if (Math.Max(Math.Abs(x - observer.X), Math.Abs(y - observer.Y)) < SightRange - 1) using (var tint = new SolidBrush(Color.FromArgb(20, 120, 205, 160))) g.FillPolygon(tint, Diamond(p.X, p.Y));
                    if (Assets != null && Assets.IsSightBlocked(tile))
                    {
                        using (var pen = new Pen(Color.FromArgb(235, 225, 90, 100), 1.5f)) g.DrawPolygon(pen, Diamond(p.X, p.Y));
                        if (Zoom >= 0.65f) DrawFlagText(g, p, tile.Flags.Contains("MF_SIGHTBLOCK") ? "S" : "TS", Color.FromArgb(255, 150, 155));
                    }
                }
                PointF source = TileToScreen(observer.X, observer.Y); using (var pen = new Pen(Color.FromArgb(130, 220, 175), 2)) g.DrawPolygon(pen, Diamond(source.X, source.Y));
                if (!export && Document.Contains(Hover.X, Hover.Y))
                {
                    PointF target = TileToScreen(Selected.X, Selected.Y);
                    if (Document.Contains(Selected.X, Selected.Y)) using (var pen = new Pen(sight.Visible(Selected.X, Selected.Y) ? Color.FromArgb(90, 205, 150) : Color.FromArgb(225, 90, 100), 1.5f)) { pen.DashStyle = DashStyle.Dash; g.DrawLine(pen, source, target); }
                }
            }
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
            {
                Tile tile = Document.At(x, y); PointF point = TileToScreen(x, y);
                if (ShowFlagOverlays) FlagOverlay.Draw(g, tile, point, Zoom, ShowFlags);
                Template trap;
                if (ShowSpikes && Assets != null && Assets.Items.TryGetValue(tile.Item, out trap) && trap.Sprite >= 20739 && trap.Sprite <= 20744)
                {
                    using (var pen = new Pen(Color.FromArgb(255, 158, 54), 1.5f)) g.DrawPolygon(pen, Diamond(point.X, point.Y));
                    FlagOverlay.DrawLabel(g, "▲", new PointF(point.X, point.Y + 8 * Zoom), Math.Max(9, 9 * Zoom), 36 * Zoom, 3, Color.FromArgb(255, 174, 72), Color.FromArgb(40, 24, 12));
                }
            }
            if (export) return;
            if (!Selection.IsEmpty) DrawArea(g, Selection, Color.FromArgb(95, 145, 205, 240));
            if (dragging && Tool == EditorTool.Rectangle && !panning) DrawArea(g, TileRect(tileStart, Hover), Color.FromArgb(105, Theme.Gold));
            if (pasteMode && clipboard != null && Document.Contains(Hover.X, Hover.Y)) DrawArea(g, new Rectangle(Hover.X, Hover.Y, clipboard.GetLength(0), clipboard.GetLength(1)), Color.FromArgb(110, 120, 205, 150));
            if (Document.Contains(Selected.X, Selected.Y)) { PointF p = TileToScreen(Selected.X, Selected.Y); using (var pen = new Pen(Theme.Gold, 2)) g.DrawPolygon(pen, Diamond(p.X, p.Y)); }
            if (Document.Contains(Hover.X, Hover.Y))
            {
                PointF p = TileToScreen(Hover.X, Hover.Y); using (var pen = new Pen(Color.FromArgb(220, 235, 240, 245))) g.DrawPolygon(pen, Diamond(p.X, p.Y));
                if (Tool == EditorTool.Paint || Tool == EditorTool.Erase)
                {
                    for (int yy = 0; yy < BrushSize; yy++) for (int xx = 0; xx < BrushSize; xx++) if (Document.Contains(Hover.X + xx, Hover.Y + yy))
                    {
                        PointF target = TileToScreen(Hover.X + xx, Hover.Y + yy);
                        using (var brush = new SolidBrush(Tool == EditorTool.Erase ? Color.FromArgb(85, 244, 121, 105) : Color.FromArgb(85, 114, 214, 172))) g.FillPolygon(brush, Diamond(target.X, target.Y));
                        if (Tool == EditorTool.Paint) DrawBrushPreview(g, target, Hover.X + xx, Hover.Y + yy);
                        Tile preview = Layer == 6 ? new Tile { Flags = BrushFlags } : Layer == 7 && WholeTileBrush != null ? WholeTileBrush : Document.At(Hover.X + xx, Hover.Y + yy);
                        FlagOverlay.Draw(g, preview, target, Zoom, true, true, Layer == 6 && FlagOperation == "Remove" ? "− " : "");
                    }
                }
                string label = Hover.X + ", " + Hover.Y;
                using (var brush = new SolidBrush(Color.FromArgb(210, Theme.Panel))) g.FillRectangle(brush, 14, Height - 36, 94, 23);
                using (var brush = new SolidBrush(Theme.Text)) g.DrawString(label, Font, brush, 20, Height - 33);
            }
            if (LowerWalls) using (var brush = new SolidBrush(Theme.Gold)) g.DrawString("WALLS LOWERED", Font, brush, 16, 14);
            if (ShowSight && sight != null) using (var brush = new SolidBrush(Color.FromArgb(130, 220, 175))) g.DrawString("SIGHT FROM " + observer.X + "," + observer.Y + "  ·  hover to move  ·  " + SightRange + " tiles", Font, brush, 16, LowerWalls ? 36 : 14);
        }
        public int ViewSprite(int id) { return Assets == null ? id : Assets.PreviewSprite(id, LowerWalls); }
        private void DrawBrushPreview(Graphics graphics, PointF point, int x, int y)
        {
            if (Assets == null || Layer == 6) return;
            spriteOpacity = 0.65f;
            try
            {
                if (Layer < 4) DrawMapSprite(graphics, BrushSprite, point, Layer >= 2, x, y);
                else if (Layer == 7 && WholeTileBrush != null)
                {
                    for (int layer = 0; layer < 4; layer++) DrawMapSprite(graphics, WholeTileBrush.Sprite(layer), point, layer >= 2, x, y);
                    Template item; if (Assets.Items.TryGetValue(WholeTileBrush.Item, out item)) DrawMapSprite(graphics, item.Sprite, point, true, x, y);
                    Template npc; if (Assets.Characters.TryGetValue(WholeTileBrush.Character, out npc)) { CharacterArt character = Assets.Rules.Npc(npc.Sprite, npc.Direction); DrawArt(graphics, Assets.NpcArt(character), point, character.Scale); }
                }
                else
                {
                    Template template;
                    if ((Layer == 4 ? Assets.Items : Assets.Characters).TryGetValue(BrushTemplate, out template))
                    { if (Layer == 4) DrawMapSprite(graphics, template.Sprite, point, true, x, y); else { CharacterArt character = Assets.Rules.Npc(template.Sprite, template.Direction); DrawArt(graphics, Assets.NpcArt(character), point, character.Scale); } }
                }
            }
            finally { spriteOpacity = 1; }
        }
        private void DrawMapSprite(Graphics g, int id, PointF p, bool canLower, int x = 0, int y = 0)
        {
            if (ShowSpikes && Assets != null && (id == 20739 || id == 20741 || id == 20743) && Assets.HasSprite(id + 1)) id++;
            if (Assets != null && id != 0) DrawArt(g, Assets.PreviewArt(id, PreferredGraphicsScale > 0 ? PreferredGraphicsScale : (int)Math.Ceiling(Zoom), x, y, canLower && LowerWalls), p, Assets.Rules.PreviewScale(id));
        }
        private void DrawFlagText(Graphics g, PointF point, string text, Color color)
        {
            Size size = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding);
            using (var backdrop = new SolidBrush(Color.FromArgb(185, 12, 14, 20))) g.FillRectangle(backdrop, point.X - size.Width / 2f - 2, point.Y - size.Height / 2f, size.Width + 4, size.Height);
            TextRenderer.DrawText(g, text, Font, new Point((int)(point.X - size.Width / 2f), (int)(point.Y - size.Height / 2f)), color, TextFormatFlags.NoPadding);
        }
        private void DrawSprite(Graphics g, int id, PointF p, float scale = 1)
        {
            if (Assets == null || id == 0) return;
            DrawArt(g, Assets.Art(id, (int)Math.Ceiling(Zoom)), p, scale);
        }
        private void DrawArt(Graphics g, SpriteArt art, PointF p, float scale = 1)
        {
            if (art == null) return;
            float factor = Zoom * scale / art.Scale;
            // A tiny shared expansion avoids nearest-sample ties when GDI+ clips
            // fractional-size sprites into different export frames.
            const float pad = 0.01f;
            var dest = new RectangleF(p.X + (-art.FullWidth / 2f + art.CropX) * factor - pad, p.Y + (-art.FullHeight / 2f + art.CropY) * factor - pad, art.Image.Width * factor + pad * 2, art.Image.Height * factor + pad * 2);
            if (dest.Right < RenderOrigin.X || dest.Bottom < RenderOrigin.Y || dest.X > RenderOrigin.X + Width || dest.Y > RenderOrigin.Y + Height) return;
            Bitmap image = EnhancedSprites && !panning && factor > 1 ? Assets.EnhancedImage(art) : art.Image;
            InterpolationMode interpolation = g.InterpolationMode; if (SmoothSprites) g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (spriteOpacity >= 1) g.DrawImage(image, dest, new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
            else using (var attributes = new ImageAttributes())
            {
                var matrix = new ColorMatrix { Matrix33 = spriteOpacity }; attributes.SetColorMatrix(matrix);
                g.DrawImage(image, new[] { new PointF(dest.X, dest.Y), new PointF(dest.Right, dest.Y), new PointF(dest.X, dest.Bottom) }, new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel, attributes);
            }
            g.InterpolationMode = interpolation;
        }
        private void Marker(Graphics g, float x, float y, Color color, string label)
        {
            using (var brush = new SolidBrush(Color.FromArgb(215, Theme.Panel))) g.FillRectangle(brush, x - 6, y - 7, 13, 14);
            using (var brush = new SolidBrush(color)) g.DrawString(label, Font, brush, x - 5, y - 8);
        }
        private void DrawArea(Graphics g, Rectangle area, Color color)
        {
            PointF a = TileToScreen(area.Left, area.Top), b = TileToScreen(area.Right - 1, area.Top), c = TileToScreen(area.Right - 1, area.Bottom - 1), d = TileToScreen(area.Left, area.Bottom - 1);
            var points = new[] { new PointF(a.X, a.Y - 10 * Zoom), new PointF(b.X + 20 * Zoom, b.Y), new PointF(c.X, c.Y + 10 * Zoom), new PointF(d.X - 20 * Zoom, d.Y) };
            using (var brush = new SolidBrush(color)) g.FillPolygon(brush, points); using (var pen = new Pen(Color.FromArgb(230, color), 2)) g.DrawPolygon(pen, points);
        }
        public void ExportViewport(string path)
        {
            using (Bitmap bitmap = RenderImage()) bitmap.Save(path, ImageFormat.Png);
        }
        public Bitmap RenderImage()
        {
            var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height)); using (Graphics graphics = Graphics.FromImage(bitmap)) { graphics.Clear(Theme.Canvas); graphics.TranslateTransform(-RenderOrigin.X, -RenderOrigin.Y); Render(graphics, true); } return bitmap;
        }
        public MapCanvas CleanRenderFrame(int width, int height, float zoom, float cameraX, float cameraY, bool smooth, int graphicsScale)
        {
            return new MapCanvas { Width = width, Height = height, Assets = Assets, Document = Document, Zoom = zoom, panX = cameraX, panY = cameraY, ShowGrid = false, ShowFlags = false, ShowForeground = true, ShowMarkers = false, ShowNpcs = ShowNpcs, LowerWalls = true, ShowSight = false, ShowCollision = false, ShowSpikes = false, SmoothSprites = smooth, PreferredGraphicsScale = graphicsScale, EnhancedSprites = false, ShowFlagOverlays = false };
        }
        public void ExportPng(string path, int multiplier, bool smooth, bool finestArtwork)
        {
            if (multiplier != 1 && multiplier != 2 && multiplier != 4) throw new ArgumentOutOfRangeException("multiplier");
            TiledMapExport.Write(this, path, Width * multiplier, Height * multiplier, Zoom * multiplier, panX * multiplier, panY * multiplier, smooth, finestArtwork ? 4 : 0);
        }
    }

    public sealed class MiniMap : Control
    {
        public MapCanvas Canvas;
        private Bitmap image;
        private Rectangle bounds;
        public MiniMap() { DoubleBuffered = true; BackColor = Theme.Panel; Cursor = Cursors.Hand; }
        public void RefreshMap()
        {
            if (image != null) image.Dispose(); image = new Bitmap(256, 256);
            if (Canvas == null || Canvas.Document == null) { Invalidate(); return; }
            var colors = new Dictionary<int, Color>();
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                Tile tile = Canvas.Document.At(x, y); int id = tile.Sprite(0); Color color;
                if (!colors.TryGetValue(id, out color))
                {
                    color = Color.FromArgb(29, 32, 38);
                    SpriteArt art = Canvas.Assets == null ? null : Canvas.Assets.PreviewArt(id);
                    if (art != null)
                    {
                        long r = 0, green = 0, b = 0, count = 0;
                        for (int yy = 0; yy < art.Image.Height; yy += 4) for (int xx = 0; xx < art.Image.Width; xx += 4) { Color pixel = art.Image.GetPixel(xx, yy); if (pixel.A < 100) continue; r += pixel.R; green += pixel.G; b += pixel.B; count++; }
                        if (count > 0) color = Color.FromArgb((int)(r / count), (int)(green / count), (int)(b / count));
                    }
                    colors[id] = color;
                }
                if (tile.Flags.Contains("MF_MOVEBLOCK")) color = Color.FromArgb(color.R / 2, color.G / 2, color.B / 2);
                if (tile.Character != "") color = Color.FromArgb(115, 170, 210);
                image.SetPixel(x, y, color);
            }
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); int size = Math.Max(1, Math.Min(Width - 12, Height - 12)); bounds = new Rectangle((Width - size) / 2, 6, size, size);
            if (image == null) return;
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor; e.Graphics.DrawImage(image, bounds);
            if (Canvas == null || Canvas.Document == null) return;
            var corners = new[] { new Point(0, 0), new Point(Canvas.Width, 0), new Point(Canvas.Width, Canvas.Height), new Point(0, Canvas.Height) };
            var points = corners.Select(p => Canvas.ScreenToTile(p)).Select(p => new PointF(bounds.X + p.X / 256f * size, bounds.Y + p.Y / 256f * size)).ToArray();
            var old = e.Graphics.Clip; e.Graphics.SetClip(bounds); using (var pen = new Pen(Theme.Gold, 1.5f)) e.Graphics.DrawPolygon(pen, points); e.Graphics.Clip = old; old.Dispose();
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Jump(e); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Jump(e); }
        private void Jump(MouseEventArgs e) { if (Canvas != null && bounds.Contains(e.Location)) Canvas.CenterOn(Math.Max(0, Math.Min(255, (e.X - bounds.X) * 256 / bounds.Width)), Math.Max(0, Math.Min(255, (e.Y - bounds.Y) * 256 / bounds.Height))); }
        protected override void Dispose(bool disposing) { if (disposing && image != null) image.Dispose(); base.Dispose(disposing); }
    }
}

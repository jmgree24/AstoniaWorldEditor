using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Forms;

namespace Astonia.Editor
{
    public sealed class SourceBrowser : Form
    {
        private readonly AssetCatalog catalog;
        private readonly DataGridView files = new DataGridView();
        private readonly TextBox filter = Theme.TextBox(400), details = Theme.TextBox(500);
        private readonly PictureBox picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Canvas };
        private readonly Button openMap = Theme.Button("Open map", 100), play = Theme.Button("Play sound", 100);
        private readonly Label heading = Theme.Label("Select a source file", 600, 45), count = Theme.Label("", 900, 26);
        private readonly Timer debounce = new Timer { Interval = 180 };
        private List<SourceFile> rows;
        private SoundPlayer player;
        private Stream sound;
        public string SelectedMap;
        public SourceBrowser(AssetCatalog catalog)
        {
            this.catalog = catalog; rows = catalog.Files;
            Text = "Resources and zones · " + catalog.Summary; ClientSize = new Size(1100, 730); MinimumSize = new Size(800, 570); StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Panel; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var browser = new TabPage("All files") { BackColor = Theme.Panel, ForeColor = Theme.Text };
            var warningPage = new TabPage("Load warnings (" + catalog.Warnings.Count + ")") { BackColor = Theme.Panel };
            var warningText = Theme.TextBox(900); warningText.Multiline = true; warningText.ReadOnly = true; warningText.ScrollBars = ScrollBars.Both; warningText.WordWrap = false; warningText.Dock = DockStyle.Fill; warningText.Text = string.Join(Environment.NewLine, catalog.Warnings); warningPage.Controls.Add(warningText);
            tabs.TabPages.Add(browser); tabs.TabPages.Add(warningPage); Controls.Add(tabs);
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 39, Padding = new Padding(8, 6, 8, 0) }; top.Controls.Add(Theme.Label("Search all paths", 110)); top.Controls.Add(filter); filter.AccessibleName = "Search resource files";
            var split = new SplitContainer { Size = new Size(1080, 650), Dock = DockStyle.Fill, BackColor = Theme.Border, Panel1MinSize = 350, Panel2MinSize = 250, SplitterDistance = 610 }; browser.Controls.Add(split); browser.Controls.Add(top);
            files.Dock = DockStyle.Fill; files.VirtualMode = true; files.ReadOnly = true; files.AllowUserToAddRows = files.AllowUserToDeleteRows = false; files.RowHeadersVisible = false; files.MultiSelect = false; files.SelectionMode = DataGridViewSelectionMode.FullRowSelect; files.BackgroundColor = Theme.Panel; files.BorderStyle = BorderStyle.None; files.EnableHeadersVisualStyles = false;
            files.DefaultCellStyle.BackColor = Theme.Control; files.DefaultCellStyle.ForeColor = Theme.Text; files.DefaultCellStyle.SelectionBackColor = Theme.Active; files.DefaultCellStyle.SelectionForeColor = Theme.Text; files.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel; files.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Gold; files.GridColor = Theme.Panel;
            files.Columns.Add("path", "Source path"); files.Columns.Add("status", "Load status"); files.Columns.Add("size", "Bytes"); files.Columns[0].Width = 340; files.Columns[1].Width = 190; files.Columns[2].Width = 85;
            foreach (DataGridViewColumn column in files.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            files.CellValueNeeded += (s, e) => { if (e.RowIndex >= rows.Count) return; SourceFile source = rows[e.RowIndex]; e.Value = e.ColumnIndex == 0 ? source.DisplayPath : e.ColumnIndex == 1 ? source.Status : source.Length.ToString("N0"); };
            files.CurrentCellChanged += (s, e) => Preview(); files.RowCount = rows.Count; split.Panel1.Controls.Add(files);
            var preview = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(10) }; split.Panel2.Controls.Add(preview);
            heading.Dock = DockStyle.Top; heading.ForeColor = Theme.Gold;
            details.Dock = DockStyle.Fill; details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Both; details.WordWrap = false; details.Font = new Font("Consolas", 9);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 39 }; buttons.Controls.Add(openMap); buttons.Controls.Add(play);
            preview.Controls.Add(details); preview.Controls.Add(picture); preview.Controls.Add(buttons); preview.Controls.Add(heading); picture.Visible = false;
            count.Dock = DockStyle.Bottom; browser.Controls.Add(count); UpdateCount();
            openMap.Click += (s, e) => { SourceFile source = Current(); if (source != null && source.Root == "zones" && source.Extension == ".map") { SelectedMap = source.FullPath; DialogResult = DialogResult.OK; Close(); } };
            play.Click += (s, e) => { try { StopAudio(); SourceFile source = Current(); if (source == null) return; sound = source.Open(); player = new SoundPlayer(sound); player.Load(); player.Play(); } catch (Exception ex) { details.Text = ex.Message; details.Visible = true; } };
            filter.TextChanged += (s, e) => { debounce.Stop(); debounce.Start(); };
            debounce.Tick += (s, e) => { debounce.Stop(); string query = filter.Text.Trim(); rows = catalog.Files.Where(f => f.DisplayPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || f.Status.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList(); files.RowCount = rows.Count; files.Invalidate(); UpdateCount(); Preview(); };
            Shown += (s, e) => { split.SplitterDistance = Math.Max(350, (int)(split.Width * 0.57)); Preview(); };
        }
        private SourceFile Current() { return files.CurrentCell == null || files.CurrentCell.RowIndex >= rows.Count ? null : rows[files.CurrentCell.RowIndex]; }
        private void UpdateCount() { count.Text = rows.Count.ToString("N0") + " files / archive entries shown  ·  " + catalog.Summary; }
        private void Preview()
        {
            StopAudio(); if (picture.Image != null) { picture.Image.Dispose(); picture.Image = null; }
            SourceFile source = Current(); openMap.Enabled = source != null && source.Root == "zones" && source.Extension == ".map"; play.Enabled = source != null && source.Extension == ".wav";
            if (source == null) return; heading.Text = source.DisplayPath + "\n" + source.Status; picture.Visible = false; details.Visible = true;
            try
            {
                if (source.Extension == ".png" || source.Extension == ".ico" || source.Extension == ".bmp" || source.Extension == ".jpg")
                {
                    using (Stream stream = source.Open()) using (Image decoded = Image.FromStream(stream)) picture.Image = new Bitmap(decoded);
                    details.Visible = false; picture.Visible = true;
                }
                else if (new[] { ".map", ".itm", ".chr", ".pre", ".json", ".ini", ".md", ".txt", ".xml", ".csv" }.Contains(source.Extension))
                {
                    string text = source.ReadText(); details.Text = text.Length > 250000 ? text.Substring(0, 250000) + "\r\n[Preview limited to 250,000 characters.]" : text;
                }
                else details.Text = "Path: " + source.DisplayPath + "\r\nSize: " + source.Length.ToString("N0") + " bytes\r\nStatus: " + source.Status + "\r\n\r\n" + (source.Extension == ".zip" ? "Archive contents appear as archive.zip!/entry paths in this browser." : source.Extension == ".wav" ? "Use Play sound to preview this WAV." : "Cataloged binary resource. This browser does not execute files.");
                if (source.Error != "") details.Text = source.Error + "\r\n\r\n" + details.Text;
            }
            catch (Exception ex) { details.Text = ex.Message; details.Visible = true; picture.Visible = false; }
        }
        private void StopAudio() { if (player != null) { player.Stop(); player.Dispose(); player = null; } if (sound != null) { sound.Dispose(); sound = null; } }
        protected override void Dispose(bool disposing) { if (disposing) { debounce.Dispose(); StopAudio(); if (picture.Image != null) picture.Image.Dispose(); } base.Dispose(disposing); }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Astonia.Editor
{
    public sealed class PngExportForm : Form
    {
        public int Multiplier = 2, OverviewWidth = 8192, NativeScale;
        public bool Smooth = true, FinestArtwork = true;
        public PngExportForm(bool overview)
        {
            Text = overview ? "Export full-map PNG" : "Export view PNG"; ClientSize = new Size(455, 255); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; BackColor = Theme.Panel; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9);
            var resolution = Theme.Combo(405); resolution.Location = new Point(20, 48);
            resolution.Items.AddRange(overview ? new[] { "4K · 4096 pixels wide", "8K · 8192 pixels wide", "16K · 16384 pixels wide", "Native 1× · 40 × 20 pixel tiles", "Native 2× · 80 × 40 pixel tiles", "Native 4× · 160 × 80 pixel tiles" } : new[] { "1× · current viewport resolution", "2× · twice the width and height", "4× · four times the width and height" }); resolution.SelectedIndex = 1;
            var smooth = Theme.Check("Smooth scaling", true); smooth.Location = new Point(18, 91);
            var finest = Theme.Check("Use finest available GX artwork (up to 4×)", true); finest.Location = new Point(18, 125);
            var description = Theme.Label("Stitched PNG · lowered walls · no flags or editor overlays.", 415, 38); description.Location = new Point(20, 161);
            resolution.SelectedIndexChanged += (s, e) => { if (overview && resolution.SelectedIndex >= 3) { smooth.Checked = false; finest.Checked = false; } };
            var export = Theme.Button("Export PNG…", 135); export.Location = new Point(290, 207);
            export.Click += (s, e) => { int selected = resolution.SelectedIndex; if (overview) { OverviewWidth = selected < 3 ? new[] { 4096, 8192, 16384 }[selected] : 0; NativeScale = selected >= 3 ? new[] { 1, 2, 4 }[selected - 3] : 0; } else Multiplier = new[] { 1, 2, 4 }[selected]; Smooth = smooth.Checked; FinestArtwork = finest.Checked; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(Theme.Label("Resolution", 400)); Controls[0].Location = new Point(20, 16); Controls.Add(resolution); Controls.Add(smooth); Controls.Add(finest); Controls.Add(description); Controls.Add(export); AcceptButton = export;
        }
    }
}

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Astonia.Editor
{
    public sealed class FlagOptionsForm : Form
    {
        public FlagOptionsForm(MapCanvas canvas)
        {
            Text = "Flag visibility & colors"; ClientSize = new Size(350, 590); BackColor = Theme.Panel; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9); StartPosition = FormStartPosition.CenterParent;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 81, Padding = new Padding(9) };
            top.Controls.Add(Theme.Label("Choose the flag markers shown on map tiles.", 330));
            var all = Theme.Button("Show all", 100); var none = Theme.Button("Hide all", 100); top.Controls.Add(all); top.Controls.Add(none);
            var rows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 6, 8, 6) };
            foreach (FlagInfo flag in EditorFlags.All)
            {
                var row = new FlowLayoutPanel { Width = 310, Height = 24, WrapContents = false, Margin = new Padding(0) };
                var check = Theme.Check(flag.Identifier + " · " + flag.Name.Substring(3), canvas.FlagOverlay.Visible.Contains(flag.Name)); check.Width = 270; check.AutoSize = false; check.Margin = new Padding(7, 0, 0, 0); check.Tag = flag.Name; check.Enabled = flag.Name != "MF_NEUTRAL";
                if (!check.Enabled) { check.Checked = false; check.Text += " (always hidden)"; }
                row.Controls.Add(new Panel { Width = 13, Height = 13, BackColor = flag.Color, Margin = new Padding(0, 4, 0, 0) }); row.Controls.Add(check); rows.Controls.Add(row);
                check.CheckedChanged += (s, e) => { if (check.Checked) canvas.FlagOverlay.Visible.Add(flag.Name); else canvas.FlagOverlay.Visible.Remove(flag.Name); canvas.ShowFlags = true; canvas.Invalidate(); };
            }
            all.Click += (s, e) => { foreach (CheckBox check in rows.Controls.Cast<Control>().SelectMany(r => r.Controls.Cast<Control>()).OfType<CheckBox>()) if (check.Enabled) check.Checked = true; };
            none.Click += (s, e) => { foreach (CheckBox check in rows.Controls.Cast<Control>().SelectMany(r => r.Controls.Cast<Control>()).OfType<CheckBox>()) check.Checked = false; canvas.ShowFlags = false; };
            Controls.Add(rows); Controls.Add(top);
            var bottom = Theme.Label("Blue underwater tint follows Map Studio's preview.\nNeutral markers are always hidden.", 340, 48); bottom.Dock = DockStyle.Bottom; bottom.Padding = new Padding(10, 0, 0, 0); Controls.Add(bottom);
        }
    }
}

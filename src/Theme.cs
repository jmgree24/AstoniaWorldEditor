using System.Drawing;
using System.Windows.Forms;

namespace Astonia.Editor
{
    public static class Theme
    {
        // Matches client/src/gui/ui_layout.c's flat Omega palette (RGB555 expanded).
        public static readonly Color Panel = Color.FromArgb(25, 25, 33), Canvas = Color.FromArgb(14, 16, 21),
            Control = Color.FromArgb(32, 33, 41), Hover = Color.FromArgb(41, 33, 33), Active = Color.FromArgb(66, 16, 16),
            Border = Color.FromArgb(96, 96, 108), Text = Color.FromArgb(239, 239, 230), Muted = Color.FromArgb(165, 165, 173),
            Gold = Color.FromArgb(220, 200, 155);
        public static Button Button(string text, int width)
        {
            var b = new Button { Text = text, Width = width, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Control,
                ForeColor = Text, Cursor = Cursors.Hand, Margin = new Padding(3, 3, 3, 3), UseVisualStyleBackColor = false, UseMnemonic = false };
            b.FlatAppearance.BorderColor = Border; b.FlatAppearance.MouseOverBackColor = Hover; b.FlatAppearance.MouseDownBackColor = Active; return b;
        }
        public static Label Label(string text, int width = 240, int height = 23)
        { return new Label { Text = text, ForeColor = Muted, Width = width, Height = height, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), UseMnemonic = false }; }
        public static ComboBox Combo(int width)
        {
            var combo = new ComboBox { Width = width, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Control, ForeColor = Text, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(3, 5, 3, 3), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 20 };
            combo.DrawItem += (s, e) =>
            {
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var background = new SolidBrush(selected ? Active : Control)) e.Graphics.FillRectangle(background, e.Bounds);
                string label = e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
                TextRenderer.DrawText(e.Graphics, label, combo.Font, e.Bounds, Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            return combo;
        }
        public static TextBox TextBox(int width)
        { return new TextBox { Width = width, BackColor = Control, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle }; }
        public static CheckBox Check(string text, bool value = false)
        { return new CheckBox { Text = text, Checked = value, AutoSize = true, ForeColor = Text, BackColor = Panel, Margin = new Padding(7, 9, 3, 3) }; }
    }
}

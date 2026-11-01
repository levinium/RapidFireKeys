using System.Windows.Forms.VisualStyles;

namespace RapidFireKeys.Views
{
    // A GroupBox whose title follows ForeColor in dark mode.
    //
    // The stock one draws its title with the visual style's own (dark) text color
    // whatever ForeColor says, and its Flat style does the same, which leaves the
    // title near-black on a dark window. In light mode this paints exactly as the
    // stock control does.
    internal sealed class ThemedGroupBox : GroupBox
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (!Theme.IsDark)
            {
                base.OnPaint(e);
                return;
            }

            var g = e.Graphics;
            var textSize = TextRenderer.MeasureText(g, Text, Font);
            int textTop = textSize.Height / 2;

            g.Clear(BackColor);
            using (var pen = new Pen(Theme.DarkBorder))
            {
                // Border, broken where the title sits
                var r = new Rectangle(0, textTop, Width - 1, Height - textTop - 1);
                int gapStart = 6, gapEnd = 8 + textSize.Width;
                g.DrawLine(pen, r.Left, r.Top, gapStart, r.Top);
                g.DrawLine(pen, Math.Min(gapEnd, r.Right), r.Top, r.Right, r.Top);
                g.DrawLine(pen, r.Left, r.Top, r.Left, r.Bottom);
                g.DrawLine(pen, r.Right, r.Top, r.Right, r.Bottom);
                g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Bottom);
            }

            TextRenderer.DrawText(g, Text, Font, new Point(8, 0), Theme.DarkText, BackColor);
        }
    }
}

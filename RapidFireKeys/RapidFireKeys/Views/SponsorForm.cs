using System.Diagnostics;

namespace RapidFireKeys.Views
{
    // The one place RapidFireKeys asks for anything.
    //
    // Reachable only from a menu item nobody has to click, so it is written to be
    // easy to say no to: no countdown, nothing pre-ticked beyond the amount
    // itself, and a door that is obviously open.
    internal sealed class SponsorForm : Form
    {
        readonly string template;
        readonly bool showAmounts;
        readonly List<RadioButton> chips = new();
        readonly RadioButton otherChip = new();
        readonly NumericUpDown otherAmount = new();
        readonly Button sponsorButton = new();

        static readonly Color accent = Color.FromArgb(191, 38, 76);

        public SponsorForm(string template)
        {
            this.template = template;

            // Only where the destination takes the amount in its link. Picking $5
            // and landing somewhere that never heard of it is worse than not being
            // asked at all.
            showAmounts = SponsorLink.CarriesAmount(template);

            Text = "Support RapidFireKeys";
            Icon = Form1.LoadTrayIcon();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 9F);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Padding = new Padding(20, 16, 20, 16),
            };

            layout.Controls.Add(new Label
            {
                Text = "♥  Support RapidFireKeys",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = accent,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10),
            });

            // What it is and what it is not. Someone deciding whether to give should
            // know they are not buying anything, and that nothing is being withheld.
            layout.Controls.Add(Paragraph("RapidFireKeys is free and open source. There is no paid version, nothing is held back, and this is the only place it asks."));
            layout.Controls.Add(Paragraph("If it has saved your fingers some button-mashing, you can put something toward keeping it working after the next game patch."));

            if (showAmounts)
                layout.Controls.Add(BuildAmounts());

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 8) };

            sponsorButton.AutoSize = true;
            sponsorButton.MinimumSize = new Size(150, 30);
            sponsorButton.ForeColor = accent;
            sponsorButton.Font = new Font(Font, FontStyle.Bold);
            sponsorButton.Click += (_, _) => Sponsor();

            var notNow = new Button
            {
                Text = "Not now",
                AutoSize = true,
                MinimumSize = new Size(90, 30),
                DialogResult = DialogResult.Cancel,
            };

            buttons.Controls.Add(sponsorButton);
            buttons.Controls.Add(notNow);
            layout.Controls.Add(buttons);

            layout.Controls.Add(new Label
            {
                Text = "Opens your browser. RapidFireKeys never sees your payment details.",
                ForeColor = SystemColors.GrayText,
                AutoSize = true,
                MaximumSize = new Size(400, 0),
            });

            Controls.Add(layout);
            AcceptButton = sponsorButton;
            CancelButton = notNow;

            UpdateChoice(null, EventArgs.Empty);
        }

        static Label Paragraph(string text) => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Margin = new Padding(0, 0, 0, 10),
        };

        Control BuildAmounts()
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };

            foreach (var amount in SponsorLink.Suggested)
            {
                var chip = new RadioButton
                {
                    Text = SponsorLink.Describe(amount),
                    Appearance = Appearance.Button,
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = true,
                    MinimumSize = new Size(54, 28),
                    Tag = amount,
                    Checked = amount == SponsorLink.Default,
                };
                chip.CheckedChanged += UpdateChoice;
                chips.Add(chip);
                row.Controls.Add(chip);
            }

            otherChip.Text = "Other";
            otherChip.Appearance = Appearance.Button;
            otherChip.TextAlign = ContentAlignment.MiddleCenter;
            otherChip.AutoSize = true;
            otherChip.MinimumSize = new Size(54, 28);
            otherChip.CheckedChanged += UpdateChoice;
            row.Controls.Add(otherChip);

            // Whole dollars only: the sponsor page's amount box truncates 7.50 to 7
            // without a word, so accepting cents would promise one number and
            // charge another. A whole-number spinner cannot be given cents.
            otherAmount.Minimum = 1;
            otherAmount.Maximum = SponsorLink.Most;
            otherAmount.DecimalPlaces = 0;
            otherAmount.Value = 15;
            otherAmount.Width = 70;
            otherAmount.Visible = false;
            otherAmount.Margin = new Padding(6, 4, 0, 0);
            otherAmount.ValueChanged += UpdateChoice;
            row.Controls.Add(otherAmount);

            return row;
        }

        decimal? Chosen =>
            !showAmounts ? null :
            otherChip.Checked ? otherAmount.Value :
            chips.FirstOrDefault(c => c.Checked)?.Tag as decimal?;

        // Naming the sum on the button means nobody has to look back at the chips
        // to check what they are about to be charged
        void UpdateChoice(object? sender, EventArgs e)
        {
            otherAmount.Visible = otherChip.Checked;
            foreach (var chip in chips.Append(otherChip))
                chip.ForeColor = chip.Checked ? accent : SystemColors.ControlText;

            sponsorButton.Text = Chosen is { } amount
                ? $"Sponsor {SponsorLink.Describe(amount)}"
                : "Open the sponsor page";
        }

        void Sponsor()
        {
            Open(Chosen is { } amount ? SponsorLink.For(template, amount) : template);
            Close();
        }

        // UseShellExecute, so this is the browser someone chose. Failures are
        // swallowed: throwing out of a donate button would be an absurd way to
        // lose the app.
        internal static void Open(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
                // Nothing to do and nothing worth interrupting anyone over.
            }
        }
    }
}

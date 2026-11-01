using RapidFireKeys.Updates;

namespace RapidFireKeys.Views
{
    // What the update check found, and the one button that acts on it.
    //
    // The release notes are shown rather than summarized, because this dialog asks
    // someone to let the app replace its own exe and restart itself. That is not a
    // thing to agree to on the strength of a version number alone.
    internal sealed class UpdateForm : Form
    {
        readonly UpdateVerdict verdict;
        readonly Action exitForUpdate;

        readonly Label heading = new();
        readonly Label subheading = new();
        readonly TextBox notes = new();
        readonly Panel progressPanel = new();
        readonly ProgressBar progressBar = new();
        readonly Label progressText = new();
        readonly Button primaryButton = new();
        readonly Button pageButton = new();
        readonly Button closeButton = new();

        CancellationTokenSource? cancel;
        bool installing;

        // exitForUpdate is called once the new version is running, to get this
        // copy out of its way
        public UpdateForm(UpdateVerdict verdict, Action exitForUpdate)
        {
            this.verdict = verdict;
            this.exitForUpdate = exitForUpdate;

            Text = "RapidFireKeys Update";
            Icon = Form1.LoadTrayIcon();
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(520, 400);
            MinimumSize = new Size(420, 260);
            MaximizeBox = false;

            BuildLayout();
            Render();
            Theme.Attach(this);
        }

        // Focus would otherwise land in the notes, which selects all of their text
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = primaryButton.Visible ? primaryButton : closeButton;
            notes.Select(0, 0);
        }

        void BuildLayout()
        {
            heading.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            heading.Dock = DockStyle.Top;
            heading.Height = 34;

            subheading.Dock = DockStyle.Top;
            subheading.AutoSize = false;
            subheading.Height = 40;
            subheading.ForeColor = SystemColors.GrayText;

            notes.Multiline = true;
            notes.ReadOnly = true;
            notes.ScrollBars = ScrollBars.Vertical;
            notes.Dock = DockStyle.Fill;
            notes.BackColor = SystemColors.Window;

            progressBar.Dock = DockStyle.Top;
            progressBar.Height = 18;
            progressText.Dock = DockStyle.Top;
            progressText.Height = 22;
            progressPanel.Dock = DockStyle.Bottom;
            progressPanel.Height = 48;
            progressPanel.Padding = new Padding(0, 8, 0, 0);
            progressPanel.Visible = false;
            progressPanel.Controls.Add(progressText);
            progressPanel.Controls.Add(progressBar);

            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 0) };
            content.Controls.Add(notes);
            content.Controls.Add(progressPanel);
            content.Controls.Add(subheading);
            content.Controls.Add(heading);

            primaryButton.Text = "Update now";
            primaryButton.AutoSize = true;
            primaryButton.MinimumSize = new Size(100, 0);
            primaryButton.Click += async (_, _) => await InstallAsync();

            pageButton.Text = "Open release page";
            pageButton.AutoSize = true;
            pageButton.Click += (_, _) =>
            {
                var url = verdict.Url ?? UpdateService.PageUrl;
                if (!string.IsNullOrWhiteSpace(url)) SponsorForm.Open(url);
            };

            closeButton.Text = "Close";
            closeButton.AutoSize = true;
            closeButton.MinimumSize = new Size(80, 0);
            closeButton.Click += (_, _) =>
            {
                // Mid-download the same button is Cancel. Nothing has been
                // replaced at that point, so stopping costs only the bytes.
                if (installing) cancel?.Cancel();
                else Close();
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(12, 10, 12, 12),
            };
            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(pageButton);
            buttons.Controls.Add(primaryButton);

            Controls.Add(content);
            Controls.Add(buttons);
            CancelButton = closeButton;
        }

        void Render()
        {
            switch (verdict.Outcome)
            {
                case UpdateOutcome.Available:
                    heading.Text = $"Version {verdict.Version} is available";
                    subheading.Text = verdict.CanInstall
                        ? $"You are running {UpdateService.CurrentVersion}. Update now downloads it, checks it, and restarts RapidFireKeys. Your CONFIG.json is kept."
                        : $"You are running {UpdateService.CurrentVersion}. This release has to be downloaded from its page.";
                    var text = ReleaseNotes.ToPlainText(verdict.Notes);
                    notes.Text = text.Length == 0 ? "No release notes were published for this version." : text;

                    // A release with no exe attached can still be announced; it
                    // just cannot be installed from here
                    primaryButton.Visible = verdict.CanInstall;
                    AcceptButton = verdict.CanInstall ? primaryButton : null;
                    break;

                case UpdateOutcome.UpToDate:
                    heading.Text = "You are up to date";
                    subheading.Text = $"Version {UpdateService.CurrentVersion} is the latest release.";
                    notes.Visible = false;
                    primaryButton.Visible = false;
                    ClientSize = new Size(ClientSize.Width, 160);
                    break;

                default:
                    heading.Text = "Could not check for updates";
                    subheading.Text = "The releases page could not be reached, or did not answer in a way this version understands. "
                        + $"You are still running {UpdateService.CurrentVersion}.";
                    notes.Visible = false;
                    primaryButton.Visible = false;
                    ClientSize = new Size(ClientSize.Width, 160);
                    break;
            }
        }

        async Task InstallAsync()
        {
            if (installing || verdict.Asset is not { } asset) return;

            installing = true;
            cancel = new CancellationTokenSource();

            primaryButton.Enabled = false;
            pageButton.Enabled = false;
            closeButton.Text = "Cancel";
            progressPanel.Visible = true;
            progressText.Text = "Starting download...";

            var size = asset.Size > 0 ? $" of {asset.Size / 1024d / 1024d:N0} MB" : string.Empty;
            var progress = new Progress<double>(fraction =>
            {
                progressBar.Value = (int)(fraction * 100);
                progressText.Text = $"Downloading... {fraction:P0}{size}";
            });

            var failure = await new UpdateInstaller().InstallAsync(asset, progress, cancel.Token);

            if (failure is null)
            {
                Log.Write($"Updated to {verdict.Version}; restarting");
                progressText.Text = "Restarting to finish the update...";
                exitForUpdate();
                return;
            }

            installing = false;
            cancel.Dispose();
            cancel = null;

            progressPanel.Visible = false;
            primaryButton.Enabled = true;
            pageButton.Enabled = true;
            closeButton.Text = "Close";

            Log.Write($"Update to {verdict.Version} failed: {failure.Message}");
            MessageBox.Show(this, failure.Message, "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

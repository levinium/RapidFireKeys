using System.Diagnostics;

namespace RapidFireKeys.Views
{
    // Picks the program hotkeys apply to: chosen from what is running now, or typed
    internal sealed class AddAppForm : Form
    {
        public string ProcessName { get; private set; } = "";

        readonly ComboBox processBox = new();
        readonly HashSet<string> existing;

        public AddAppForm(IEnumerable<string> existingApps)
        {
            existing = new HashSet<string>(existingApps, StringComparer.OrdinalIgnoreCase);

            Text = "Add app";
            Icon = Form1.LoadTrayIcon();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(400, 150);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(12),
            };

            var prompt = new Label
            {
                Text = "Pick a running program, or type its .exe name (for example D2R.exe):",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };

            processBox.DropDownStyle = ComboBoxStyle.DropDown;
            processBox.Dock = DockStyle.Top;
            processBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            processBox.AutoCompleteSource = AutoCompleteSource.ListItems;
            processBox.Items.AddRange(RunningWindowedApps().ToArray<object>());

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                AutoSize = true,
            };
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 0), DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "Add", AutoSize = true, MinimumSize = new Size(80, 0) };
            ok.Click += (_, _) => Accept();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            AcceptButton = ok;
            CancelButton = cancel;

            layout.Controls.Add(prompt);
            layout.Controls.Add(processBox);
            Controls.Add(layout);
            Controls.Add(buttons);

            Theme.Attach(this);
        }

        // Programs with a visible window: the ones a person could be playing.
        // RapidFireKeys itself is left out, since it never needs hotkeys.
        static IEnumerable<string> RunningWindowedApps()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int self = Environment.ProcessId;

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id != self && process.MainWindowHandle != IntPtr.Zero)
                        names.Add(process.ProcessName + ".exe");
                }
                catch
                {
                    // Exited while being looked at
                }
                finally
                {
                    process.Dispose();
                }
            }

            return names;
        }

        void Accept()
        {
            string name = HotkeyConfig.NormalizeProcessName(processBox.Text);

            if (name.Length == 0 || name == ".exe" || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "Enter the program's file name, like D2R.exe.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (existing.Contains(name))
            {
                MessageBox.Show(this, $"{name} is already in the list.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ProcessName = name;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

using System.ComponentModel;

namespace RapidFireKeys.Views
{
    // Edits CONFIG.json and the app's own settings, so nobody has to open a JSON
    // file to change which keys rapid-fire.
    //
    // Works on a copy: nothing reaches CONFIG.json or the running app until Save,
    // and Cancel leaves both exactly as they were.
    internal sealed class SettingsForm : Form
    {
        // One row in the hotkey grid. Shift/Ctrl/Alt are the modifiers people
        // actually use; any other modifier a hand-edited CONFIG.json asks for is
        // carried through untouched in Extra rather than dropped on save.
        sealed class HotkeyRow
        {
            public string Key { get; set; } = "";
            public bool Shift { get; set; }
            public bool Ctrl { get; set; }
            public bool Alt { get; set; }
            public List<string> Extra { get; } = new();
        }

        sealed class AppEntry
        {
            public string Process { get; set; } = "";
            public BindingList<HotkeyRow> Hotkeys { get; } = new();
            public override string ToString() => Process;
        }

        readonly Preferences preferences;
        readonly Action<Dictionary<string, List<KeyBinding>>> applyBindings;
        readonly Action<IWin32Window> checkForUpdates;
        readonly Action<IWin32Window>? openSponsor;

        readonly BindingList<AppEntry> apps = new();

        readonly ListBox appList = new();
        readonly DataGridView grid = new();
        readonly GroupBox hotkeyGroup = new();
        readonly Label hotkeyHint = new();
        readonly Button removeAppButton = new();
        readonly Button addHotkeyButton = new();
        readonly Button removeHotkeyButton = new();
        readonly CheckBox autoUpdateCheck = new();
        readonly Label warningLabel = new();

        // The grid stores the CONFIG.json name and shows a friendlier label
        sealed record KeyChoice(string Name, string Label);

        static readonly KeyChoice[] keyChoices = Enum.GetValues<Keys>()
            .Select(k => new KeyChoice(k.ToString(), Describe(k)))
            .ToArray();

        internal static string Describe(Keys key) => key switch
        {
            Keys.LButton => "Left mouse button",
            Keys.RButton => "Right mouse button",
            Keys.MButton => "Middle mouse button",
            Keys.XButton1 => "Mouse button 4 (back)",
            Keys.XButton2 => "Mouse button 5 (forward)",
            Keys.Back => "Backspace",
            Keys.Esc => "Escape",
            Keys.Apps => "Menu",
            Keys.LWin => "Left Windows",
            Keys.RWin => "Right Windows",
            Keys.Multiply => "Numpad *",
            Keys.Add => "Numpad +",
            Keys.Subtract => "Numpad -",
            Keys.Decimal => "Numpad .",
            Keys.Divide => "Numpad /",
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            >= Keys.Numpad0 and <= Keys.Numpad9 => $"Numpad {key - Keys.Numpad0}",
            _ => key.ToString(),
        };

        public SettingsForm(
            Preferences preferences,
            Action<Dictionary<string, List<KeyBinding>>> applyBindings,
            Action<IWin32Window> checkForUpdates,
            Action<IWin32Window>? openSponsor)
        {
            this.preferences = preferences;
            this.applyBindings = applyBindings;
            this.checkForUpdates = checkForUpdates;
            this.openSponsor = openSponsor;

            Text = "RapidFireKeys Settings";
            Icon = Form1.LoadTrayIcon();
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(720, 540);
            MinimumSize = new Size(620, 460);
            ShowInTaskbar = true;

            BuildLayout();
        }

        // Loaded here rather than in the constructor: the app list is data-bound,
        // and binding does not fill a ListBox until its window exists
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LoadCurrentConfig();
        }

        void BuildLayout()
        {
            // Warning strip, only shown when CONFIG.json could not be read
            warningLabel.Dock = DockStyle.Top;
            warningLabel.AutoSize = false;
            warningLabel.Height = 40;
            warningLabel.Padding = new Padding(8, 4, 8, 4);
            warningLabel.BackColor = Color.FromArgb(255, 244, 206);
            warningLabel.ForeColor = Color.FromArgb(90, 60, 0);
            warningLabel.Visible = false;

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(10, 10, 10, 0),
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            body.Controls.Add(BuildAppsGroup(), 0, 0);
            body.Controls.Add(BuildHotkeysGroup(), 1, 0);

            var updatesGroup = BuildUpdatesGroup();
            body.Controls.Add(updatesGroup, 0, 1);
            body.SetColumnSpan(updatesGroup, 2);

            Controls.Add(body);
            Controls.Add(warningLabel);
            Controls.Add(BuildFooter());
        }

        GroupBox BuildAppsGroup()
        {
            var group = new GroupBox { Text = "Apps", Dock = DockStyle.Fill, Padding = new Padding(8) };

            appList.Dock = DockStyle.Fill;
            appList.IntegralHeight = false;
            appList.DataSource = apps;
            appList.SelectedIndexChanged += (_, _) => ShowSelectedApp();

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(0, 6, 0, 0),
            };

            var addAppButton = new Button { Text = "Add app...", AutoSize = true };
            addAppButton.Click += (_, _) => AddApp();

            removeAppButton.Text = "Remove";
            removeAppButton.AutoSize = true;
            removeAppButton.Click += (_, _) => RemoveApp();

            buttons.Controls.Add(addAppButton);
            buttons.Controls.Add(removeAppButton);

            var hint = new Label
            {
                Text = "Hotkeys only work while one of these apps is in front.",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 36,
                ForeColor = SystemColors.GrayText,
            };

            group.Controls.Add(appList);
            group.Controls.Add(hint);
            group.Controls.Add(buttons);
            return group;
        }

        GroupBox BuildHotkeysGroup()
        {
            hotkeyGroup.Text = "Hotkeys";
            hotkeyGroup.Dock = DockStyle.Fill;
            hotkeyGroup.Padding = new Padding(8);

            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.BackgroundColor = SystemColors.Window;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                HeaderText = "Key",
                DataPropertyName = nameof(HotkeyRow.Key),
                DataSource = keyChoices,
                ValueMember = nameof(KeyChoice.Name),
                DisplayMember = nameof(KeyChoice.Label),
                FlatStyle = FlatStyle.Flat,
                FillWeight = 160,
            });
            grid.Columns.Add(CheckColumn("Needs Shift", nameof(HotkeyRow.Shift)));
            grid.Columns.Add(CheckColumn("Needs Ctrl", nameof(HotkeyRow.Ctrl)));
            grid.Columns.Add(CheckColumn("Needs Alt", nameof(HotkeyRow.Alt)));

            // Commit a checkbox as soon as it is clicked, not when focus leaves the cell
            grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.DataError += (_, e) => e.ThrowException = false;
            grid.SelectionChanged += (_, _) => UpdateButtons();
            grid.KeyDown += (_, e) =>
            {
                if (e.KeyCode == System.Windows.Forms.Keys.Delete && !grid.IsCurrentCellInEditMode)
                {
                    RemoveHotkeys();
                    e.Handled = true;
                }
            };

            hotkeyHint.Text = "Hold a key for a moment and it repeats until you let go. A ticked Shift, Ctrl or Alt means the key only rapid-fires while that is held too.";
            hotkeyHint.Dock = DockStyle.Top;
            hotkeyHint.AutoSize = false;
            hotkeyHint.Height = 36;
            hotkeyHint.ForeColor = SystemColors.GrayText;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(0, 6, 0, 0),
            };

            addHotkeyButton.Text = "Add hotkey...";
            addHotkeyButton.AutoSize = true;
            addHotkeyButton.Click += (_, _) => AddHotkey();

            removeHotkeyButton.Text = "Remove";
            removeHotkeyButton.AutoSize = true;
            removeHotkeyButton.Click += (_, _) => RemoveHotkeys();

            buttons.Controls.Add(addHotkeyButton);
            buttons.Controls.Add(removeHotkeyButton);

            hotkeyGroup.Controls.Add(grid);
            hotkeyGroup.Controls.Add(hotkeyHint);
            hotkeyGroup.Controls.Add(buttons);
            return hotkeyGroup;
        }

        static DataGridViewCheckBoxColumn CheckColumn(string header, string property) => new()
        {
            HeaderText = header,
            DataPropertyName = property,
            FillWeight = 70,
        };

        GroupBox BuildUpdatesGroup()
        {
            var group = new GroupBox
            {
                Text = "Updates",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(8, 4, 8, 8),
            };

            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };

            autoUpdateCheck.Text = "Check for updates automatically (once a day)";
            autoUpdateCheck.AutoSize = true;
            autoUpdateCheck.Checked = preferences.CheckForUpdatesAutomatically;
            autoUpdateCheck.Margin = new Padding(3, 7, 12, 3);
            autoUpdateCheck.Enabled = Updates.UpdateService.IsAvailable;

            var checkNow = new Button { Text = "Check now", AutoSize = true };
            checkNow.Click += (_, _) => checkForUpdates(this);

            var version = new Label
            {
                Text = $"You are running v{Updates.UpdateService.CurrentVersion}",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(12, 8, 3, 3),
            };

            row.Controls.Add(autoUpdateCheck);
            row.Controls.Add(checkNow);
            row.Controls.Add(version);
            group.Controls.Add(row);
            return group;
        }

        Control BuildFooter()
        {
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(10, 8, 10, 10),
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };

            var defaultsButton = new Button { Text = "Restore defaults", AutoSize = true };
            defaultsButton.Click += (_, _) => RestoreDefaults();
            left.Controls.Add(defaultsButton);

            // Only in builds published with a sponsor link; see SponsorLink
            if (openSponsor != null)
            {
                var sponsorButton = new Button
                {
                    Text = "♥ Support RapidFireKeys",
                    AutoSize = true,
                    ForeColor = Color.FromArgb(191, 38, 76),
                };
                sponsorButton.Click += (_, _) => openSponsor(this);
                left.Controls.Add(sponsorButton);
            }

            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };

            var saveButton = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(80, 0) };
            saveButton.Click += (_, _) => Save();

            var cancelButton = new Button
            {
                Text = "Cancel",
                AutoSize = true,
                MinimumSize = new Size(80, 0),
                DialogResult = DialogResult.Cancel,
            };

            right.Controls.Add(saveButton);
            right.Controls.Add(cancelButton);

            AcceptButton = saveButton;
            CancelButton = cancelButton;

            footer.Controls.Add(left, 0, 0);
            footer.Controls.Add(right, 2, 0);
            return footer;
        }

        void LoadCurrentConfig()
        {
            var (bindings, error) = HotkeyConfig.Load();
            if (error != null)
            {
                warningLabel.Text = $"CONFIG.json could not be read ({error}). The defaults are shown instead; Save replaces the file.";
                warningLabel.Visible = true;
                bindings = HotkeyConfig.Defaults();
            }
            Populate(bindings);
        }

        void Populate(Dictionary<string, List<KeyBinding>> bindings)
        {
            apps.RaiseListChangedEvents = false;
            apps.Clear();

            foreach (var (process, list) in bindings)
            {
                var entry = new AppEntry { Process = process };
                foreach (var bind in list)
                {
                    var row = new HotkeyRow
                    {
                        Key = HotkeyConfig.TryParseKey(bind.Key, out Keys key) ? key.ToString() : bind.Key,
                    };
                    foreach (var mod in bind.Modifiers ?? new())
                    {
                        HotkeyConfig.TryParseKey(mod, out Keys modKey);
                        switch (modKey)
                        {
                            case Keys.Shift: row.Shift = true; break;
                            case Keys.Ctrl: row.Ctrl = true; break;
                            case Keys.Alt: row.Alt = true; break;
                            default: row.Extra.Add(mod); break;
                        }
                    }
                    entry.Hotkeys.Add(row);
                }
                apps.Add(entry);
            }

            apps.RaiseListChangedEvents = true;
            apps.ResetBindings();
            appList.SelectedIndex = appList.Items.Count > 0 ? 0 : -1;
            ShowSelectedApp();
        }

        AppEntry? SelectedApp => appList.SelectedItem as AppEntry;

        void ShowSelectedApp()
        {
            grid.EndEdit();
            var app = SelectedApp;
            grid.DataSource = app?.Hotkeys;
            hotkeyGroup.Text = app != null ? $"Hotkeys for {app.Process}" : "Hotkeys";
            UpdateButtons();
        }

        void UpdateButtons()
        {
            bool hasApp = SelectedApp != null;
            removeAppButton.Enabled = hasApp;
            addHotkeyButton.Enabled = hasApp;
            grid.Enabled = hasApp;
            removeHotkeyButton.Enabled = hasApp && grid.SelectedRows.Count > 0;
        }

        void AddApp()
        {
            using var dialog = new AddAppForm(apps.Select(a => a.Process));
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            var entry = new AppEntry { Process = dialog.ProcessName };
            apps.Add(entry);
            appList.SelectedItem = entry;
            ShowSelectedApp();

            // An app with no hotkeys does nothing, so go straight to adding one
            AddHotkey();
        }

        void RemoveApp()
        {
            if (SelectedApp is not { } app) return;

            if (app.Hotkeys.Count > 0 && MessageBox.Show(this,
                    $"Stop rapid-firing in {app.Process} and remove its {app.Hotkeys.Count} hotkey(s)?",
                    "Remove app", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            apps.Remove(app);
            ShowSelectedApp();
        }

        void AddHotkey()
        {
            if (SelectedApp is not { } app) return;

            using var dialog = new CaptureKeyForm(app.Process);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.CapturedKey is not { } key) return;

            string name = key.ToString();
            int existing = IndexOfKey(app, name);
            if (existing >= 0)
            {
                SelectRow(existing);
                MessageBox.Show(this, $"{Describe(key)} is already a hotkey for {app.Process}.",
                    "Add hotkey", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            app.Hotkeys.Add(new HotkeyRow
            {
                Key = name,
                Shift = dialog.Shift && key != Keys.Shift,
                Ctrl = dialog.Ctrl && key != Keys.Ctrl,
                Alt = dialog.Alt && key != Keys.Alt,
            });
            SelectRow(app.Hotkeys.Count - 1);
        }

        static int IndexOfKey(AppEntry app, string name)
        {
            for (int i = 0; i < app.Hotkeys.Count; i++)
            {
                if (string.Equals(app.Hotkeys[i].Key, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        void SelectRow(int index)
        {
            grid.ClearSelection();
            if (index < 0 || index >= grid.Rows.Count) return;
            grid.Rows[index].Selected = true;
            grid.FirstDisplayedScrollingRowIndex = index;
        }

        void RemoveHotkeys()
        {
            if (SelectedApp is not { } app) return;

            var rows = grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.DataBoundItem)
                .OfType<HotkeyRow>()
                .ToList();
            foreach (var row in rows)
                app.Hotkeys.Remove(row);
            UpdateButtons();
        }

        void RestoreDefaults()
        {
            if (MessageBox.Show(this,
                    "Replace every app and hotkey with the defaults (Diablo II: Resurrected with its usual skill keys)?\n\nNothing is saved until you press Save.",
                    "Restore defaults", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Populate(HotkeyConfig.Defaults());
        }

        // Rebuilds the CONFIG.json form of what is on screen, or explains why it can't
        Dictionary<string, List<KeyBinding>>? Collect()
        {
            grid.EndEdit();
            var result = new Dictionary<string, List<KeyBinding>>();

            foreach (var app in apps)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var list = new List<KeyBinding>();

                foreach (var row in app.Hotkeys)
                {
                    // Two rows for one key would start two repeats for a single held key
                    if (!seen.Add(row.Key))
                    {
                        appList.SelectedItem = app;
                        SelectRow(app.Hotkeys.IndexOf(row));
                        MessageBox.Show(this, $"{(HotkeyConfig.TryParseKey(row.Key, out Keys k) ? Describe(k) : row.Key)} is listed twice for {app.Process}. Remove one before saving.",
                            "Save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return null;
                    }

                    var modifiers = new List<string>();
                    if (row.Shift) modifiers.Add(nameof(Keys.Shift));
                    if (row.Ctrl) modifiers.Add(nameof(Keys.Ctrl));
                    if (row.Alt) modifiers.Add(nameof(Keys.Alt));
                    modifiers.AddRange(row.Extra);

                    list.Add(new KeyBinding { Key = row.Key, Modifiers = modifiers.Count > 0 ? modifiers : null });
                }

                result[app.Process] = list;
            }

            return result;
        }

        void Save()
        {
            var bindings = Collect();
            if (bindings == null) return;

            try
            {
                HotkeyConfig.Save(bindings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"CONFIG.json could not be saved: {ex.Message}",
                    "Save", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            preferences.CheckForUpdatesAutomatically = autoUpdateCheck.Checked;
            preferences.Save();

            applyBindings(bindings);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

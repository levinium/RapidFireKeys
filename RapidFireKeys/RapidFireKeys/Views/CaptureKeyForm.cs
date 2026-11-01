namespace RapidFireKeys.Views
{
    // "Press the key you want to rapid-fire": captures one key or mouse button,
    // plus whichever of Shift/Ctrl/Alt were held with it.
    //
    // Mouse buttons are only captured over the target area, so the Cancel button
    // stays clickable. Shift, Ctrl and Alt on their own are captured when released
    // with nothing pressed alongside, since pressing one is how every combination
    // starts.
    internal sealed class CaptureKeyForm : Form
    {
        public Keys? CapturedKey { get; private set; }
        public bool Shift { get; private set; }
        public bool Ctrl { get; private set; }
        public bool Alt { get; private set; }

        readonly Label target = new();
        readonly Label status = new();

        // Set once a non-modifier key goes down, so releasing Shift after Shift+Q
        // does not then capture Shift as well
        bool comboPressed;

        public CaptureKeyForm(string processName)
        {
            Text = "Add hotkey";
            Icon = Form1.LoadTrayIcon();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(420, 230);
            KeyPreview = true;

            var heading = new Label
            {
                Text = $"Press the key to rapid-fire in {processName}",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(12, 10, 12, 0),
            };

            target.Text = "Press a key, or click here with the mouse button you want.\n\nHold Shift, Ctrl or Alt as well if the key should only rapid-fire with them.";
            target.TextAlign = ContentAlignment.MiddleCenter;
            target.BorderStyle = BorderStyle.FixedSingle;
            target.BackColor = SystemColors.Window;
            target.Dock = DockStyle.Fill;
            target.Cursor = Cursors.Hand;
            target.MouseDown += Target_MouseDown;

            // Right-click would otherwise open nothing useful, but suppress it anyway
            target.ContextMenuStrip = new ContextMenuStrip();

            var targetHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
            targetHolder.Controls.Add(target);

            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Color.Firebrick;

            var cancel = new Button
            {
                Text = "Cancel",
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Right,
                TabStop = false,
            };

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                ColumnCount = 2,
                Padding = new Padding(12, 0, 12, 8),
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(status, 0, 0);
            footer.Controls.Add(cancel, 1, 0);

            Controls.Add(targetHolder);
            Controls.Add(heading);
            Controls.Add(footer);
        }

        // Every key goes through here first, including Tab, Enter, Esc and the
        // arrows that would otherwise move focus or close the dialog
        protected override bool ProcessCmdKey(ref Message msg, System.Windows.Forms.Keys keyData)
        {
            const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
            if (msg.Msg != WM_KEYDOWN && msg.Msg != WM_SYSKEYDOWN)
                return base.ProcessCmdKey(ref msg, keyData);

            var code = keyData & System.Windows.Forms.Keys.KeyCode;
            if (IsModifier(code))
                return true;

            comboPressed = true;
            Capture((int)code, keyData);
            return true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (comboPressed || !IsModifier(e.KeyCode)) return;

            // A modifier pressed and released on its own is the key to rapid-fire
            int vk = e.KeyCode switch
            {
                System.Windows.Forms.Keys.ShiftKey => (int)Keys.Shift,
                System.Windows.Forms.Keys.ControlKey => (int)Keys.Ctrl,
                _ => (int)Keys.Alt,
            };
            Capture(vk, System.Windows.Forms.Keys.None);
            e.Handled = true;
        }

        // Alt alone would otherwise put focus on the (absent) menu bar
        protected override void WndProc(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112, SC_KEYMENU = 0xF100;
            if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_KEYMENU)
                return;
            base.WndProc(ref m);
        }

        void Target_MouseDown(object? sender, MouseEventArgs e)
        {
            Keys? key = e.Button switch
            {
                MouseButtons.Left => Keys.LButton,
                MouseButtons.Right => Keys.RButton,
                MouseButtons.Middle => Keys.MButton,
                MouseButtons.XButton1 => Keys.XButton1,
                MouseButtons.XButton2 => Keys.XButton2,
                _ => null,
            };
            if (key is { } k)
                Capture((int)k, ModifierKeys);
        }

        void Capture(int vk, System.Windows.Forms.Keys modifiers)
        {
            if (!Enum.IsDefined(typeof(Keys), vk))
            {
                status.Text = "That key can't be rapid-fired. Try another.";
                comboPressed = false;
                return;
            }

            CapturedKey = (Keys)vk;
            Shift = modifiers.HasFlag(System.Windows.Forms.Keys.Shift);
            Ctrl = modifiers.HasFlag(System.Windows.Forms.Keys.Control);
            Alt = modifiers.HasFlag(System.Windows.Forms.Keys.Alt);
            DialogResult = DialogResult.OK;
            Close();
        }

        static bool IsModifier(System.Windows.Forms.Keys code) =>
            code is System.Windows.Forms.Keys.ShiftKey
                 or System.Windows.Forms.Keys.ControlKey
                 or System.Windows.Forms.Keys.Menu
                 or System.Windows.Forms.Keys.LShiftKey
                 or System.Windows.Forms.Keys.RShiftKey
                 or System.Windows.Forms.Keys.LControlKey
                 or System.Windows.Forms.Keys.RControlKey
                 or System.Windows.Forms.Keys.LMenu
                 or System.Windows.Forms.Keys.RMenu;
    }
}

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RapidFireKeys
{
    internal enum ThemeMode { System, Light, Dark }

    // Light and dark appearance for every window and the tray menu.
    //
    // .NET 8 WinForms has no dark mode of its own, so this recolors controls
    // directly. Each control's original colors are recorded the first time it is
    // themed and classified by role (window background, input field, muted text,
    // a custom accent...), so switching back to light restores exactly what the
    // window was built with, and switching again maps the same roles to dark.
    internal static class Theme
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const uint SWP_FRAMECHANGED = 0x0020, SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

        // Dark palette, close to Windows 11's own dark surfaces
        public static readonly Color DarkBack = Color.FromArgb(32, 32, 32);
        public static readonly Color DarkSurface = Color.FromArgb(43, 43, 43);
        public static readonly Color DarkButton = Color.FromArgb(55, 55, 55);
        public static readonly Color DarkButtonHover = Color.FromArgb(70, 70, 70);
        public static readonly Color DarkBorder = Color.FromArgb(85, 85, 85);
        public static readonly Color DarkText = Color.FromArgb(235, 235, 235);
        public static readonly Color DarkMuted = Color.FromArgb(160, 160, 160);
        public static readonly Color DarkSelection = Color.FromArgb(0, 95, 184);

        static ThemeMode mode = ThemeMode.System;
        static bool lastIsDark;

        // Raised on the UI thread when the effective theme flips, whether from the
        // setting or from Windows switching between light and dark
        public static event Action? Changed;

        static Theme()
        {
            lastIsDark = IsDark;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
                    RaiseIfChanged();
            };
        }

        public static ThemeMode Mode
        {
            get => mode;
            set
            {
                mode = value;
                RaiseIfChanged();
            }
        }

        public static bool IsDark => mode == ThemeMode.Dark || (mode == ThemeMode.System && WindowsUsesDarkApps());

        public static Color Text => IsDark ? DarkText : SystemColors.ControlText;
        public static Color Muted => IsDark ? DarkMuted : SystemColors.GrayText;

        // The "apps" half of Windows' light/dark choice (Settings > Personalization > Colors)
        static bool WindowsUsesDarkApps()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch
            {
                return false;
            }
        }

        static void RaiseIfChanged()
        {
            bool dark = IsDark;
            if (dark == lastIsDark) return;
            lastIsDark = dark;
            Changed?.Invoke();
        }

        // Themes the form now and whenever the theme changes, for as long as it lives.
        // afterApply runs after each pass, for colors a window manages itself.
        public static void Attach(Form form, Action? afterApply = null)
        {
            void Reapply()
            {
                if (form.IsDisposed) return;
                if (form.InvokeRequired)
                {
                    form.BeginInvoke(Reapply);
                    return;
                }
                Apply(form);
                afterApply?.Invoke();
            }

            Apply(form);
            afterApply?.Invoke();

            // Title bar and scrollbars need window handles, which exist only once shown
            form.HandleCreated += (_, _) => SetDarkTitleBar(form, IsDark);
            form.Shown += (_, _) => ApplyNative(form, IsDark);

            Changed += Reapply;
            form.Disposed += (_, _) => Changed -= Reapply;
        }

        public static void Apply(Form form)
        {
            bool dark = IsDark;

            // Record every control's original colors before the form's change, since
            // most controls inherit the form's colors and would otherwise be recorded
            // as already dark
            RecordTree(form);

            if (dark)
            {
                form.BackColor = DarkBack;
                form.ForeColor = DarkText;
            }
            else
            {
                form.ResetBackColor();
                form.ResetForeColor();
            }

            foreach (Control child in form.Controls)
                ApplyTree(child, dark);

            if (form.IsHandleCreated)
            {
                SetDarkTitleBar(form, dark);
                ApplyNative(form, dark);
            }
        }

        // ---- Per-control colors ----

        enum BackRole { Inherit, Input, Custom }
        enum ForeRole { Inherit, Muted, Custom }

        sealed class Original
        {
            public BackRole Back;
            public ForeRole Fore;
            public Color BackColor;
            public Color ForeColor;
            public FlatStyle? FlatStyle;
            public BorderStyle? Border;
        }

        static readonly ConditionalWeakTable<Control, Original> originals = new();

        static Original Record(Control c)
        {
            return originals.GetValue(c, control =>
            {
                var o = new Original { BackColor = control.BackColor, ForeColor = control.ForeColor };

                o.Back = control.BackColor == SystemColors.Control ? BackRole.Inherit
                    : control.BackColor == SystemColors.Window ? BackRole.Input
                    : BackRole.Custom;

                o.Fore = control.ForeColor == SystemColors.ControlText || control.ForeColor == SystemColors.WindowText ? ForeRole.Inherit
                    : control.ForeColor == SystemColors.GrayText ? ForeRole.Muted
                    : ForeRole.Custom;

                o.FlatStyle = control switch
                {
                    ButtonBase b => b.FlatStyle,
                    ComboBox cb => cb.FlatStyle,
                    _ => null,
                };
                o.Border = control switch
                {
                    TextBoxBase t => t.BorderStyle,
                    ListBox l => l.BorderStyle,
                    UpDownBase u => u.BorderStyle,
                    _ => null,
                };
                return o;
            });
        }

        static void RecordTree(Control root)
        {
            foreach (Control c in root.Controls)
            {
                Record(c);
                RecordTree(c);
            }
        }

        static void ApplyTree(Control c, bool dark)
        {
            var o = Record(c);

            switch (o.Back)
            {
                case BackRole.Inherit:
                    c.ResetBackColor();
                    break;
                case BackRole.Input:
                    c.BackColor = dark ? DarkSurface : o.BackColor;
                    break;
                case BackRole.Custom:
                    c.BackColor = dark ? Blend(o.BackColor, DarkBack, 0.25) : o.BackColor;
                    break;
            }

            switch (o.Fore)
            {
                // Input fields are not reliably drawn in an inherited color (a
                // read-only TextBox falls back to grey), so theirs is set outright
                case ForeRole.Inherit when dark && o.Back == BackRole.Input:
                    c.ForeColor = DarkText;
                    break;
                case ForeRole.Inherit:
                    c.ResetForeColor();
                    break;
                case ForeRole.Muted:
                    c.ForeColor = dark ? DarkMuted : o.ForeColor;
                    break;
                case ForeRole.Custom:
                    c.ForeColor = dark ? Lighten(o.ForeColor) : o.ForeColor;
                    break;
            }

            switch (c)
            {
                case ButtonBase button when o.FlatStyle is { } style:
                    StyleButton(button, dark, style);
                    break;
                // A themed (non-flat) combo box takes its dark look from the
                // DarkMode_CFD visual style set in ApplyNative
                case ComboBox combo when o.FlatStyle is { } style:
                    combo.FlatStyle = style;
                    break;
                case TextBoxBase text when o.Border is { } border:
                    text.BorderStyle = dark ? BorderStyle.FixedSingle : border;
                    break;
                case ListBox list when o.Border is { } border:
                    list.BorderStyle = dark ? BorderStyle.FixedSingle : border;
                    break;
                case UpDownBase upDown when o.Border is { } border:
                    upDown.BorderStyle = dark ? BorderStyle.FixedSingle : border;
                    break;
                case DataGridView grid:
                    StyleGrid(grid, dark);
                    break;
            }

            foreach (Control child in c.Controls)
                ApplyTree(child, dark);
        }

        static void StyleButton(ButtonBase button, bool dark, FlatStyle original)
        {
            // Check boxes and radio buttons drawn as boxes keep the window's
            // background; push-style ones (buttons, the support amount chips) get
            // a raised surface
            bool pushStyle = button is Button
                || (button is CheckBox cb && cb.Appearance == Appearance.Button)
                || (button is RadioButton rb && rb.Appearance == Appearance.Button);

            if (!dark)
            {
                button.FlatStyle = original;
                if (pushStyle && button.BackColor == DarkButton) button.ResetBackColor();
                if (button is Button or CheckBox or RadioButton) button.UseVisualStyleBackColor = true;
                return;
            }

            button.FlatStyle = FlatStyle.Flat;
            if (pushStyle)
            {
                button.BackColor = DarkButton;
                button.FlatAppearance.BorderColor = DarkBorder;
                button.FlatAppearance.MouseOverBackColor = DarkButtonHover;
                button.FlatAppearance.MouseDownBackColor = DarkBorder;
                button.FlatAppearance.CheckedBackColor = Color.FromArgb(70, 40, 50);
            }
        }

        static void StyleGrid(DataGridView grid, bool dark)
        {
            grid.EnableHeadersVisualStyles = !dark;
            grid.BackgroundColor = dark ? DarkSurface : SystemColors.Window;
            grid.GridColor = dark ? DarkBorder : SystemColors.ControlDark;

            var cell = grid.DefaultCellStyle;
            cell.BackColor = dark ? DarkSurface : SystemColors.Window;
            cell.ForeColor = dark ? DarkText : SystemColors.ControlText;
            cell.SelectionBackColor = dark ? DarkSelection : SystemColors.Highlight;
            cell.SelectionForeColor = dark ? Color.White : SystemColors.HighlightText;

            var header = grid.ColumnHeadersDefaultCellStyle;
            header.BackColor = dark ? DarkButton : SystemColors.Control;
            header.ForeColor = dark ? DarkText : SystemColors.ControlText;
            header.SelectionBackColor = header.BackColor;
            header.SelectionForeColor = header.ForeColor;
            grid.ColumnHeadersBorderStyle = dark ? DataGridViewHeaderBorderStyle.Single : DataGridViewHeaderBorderStyle.Raised;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column is DataGridViewCheckBoxColumn check)
                    check.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                // The drop-down arrow is always drawn light, so in dark mode it is
                // shown only on the cell being edited
                if (column is DataGridViewComboBoxColumn combo)
                    combo.DisplayStyleForCurrentCellOnly = dark;
            }
        }

        // ---- Native parts: title bar and scrollbars ----

        static void SetDarkTitleBar(Form form, bool dark)
        {
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
                // Repaint the frame now rather than on the next activation
                SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch
            {
                // Older Windows: the title bar simply stays light
            }
        }

        static void ApplyNative(Control root, bool dark)
        {
            foreach (Control c in root.Controls)
            {
                if (c.IsHandleCreated && c is TextBoxBase or ListBox or DataGridView or ScrollBar or ComboBox)
                {
                    string theme = c is ComboBox ? (dark ? "DarkMode_CFD" : "CFD") : (dark ? "DarkMode_Explorer" : "Explorer");
                    try { SetWindowTheme(c.Handle, theme, null); } catch { }
                    c.Invalidate(true);
                }
                ApplyNative(c, dark);
            }
        }

        // ---- Tray menu ----

        public static void ApplyMenu(ToolStrip menu)
        {
            bool dark = IsDark;
            menu.Renderer = dark ? new ToolStripProfessionalRenderer(new DarkMenuColors()) : new ToolStripProfessionalRenderer();
            foreach (ToolStripItem item in menu.Items)
            {
                if (dark) item.ForeColor = DarkText;
                else item.ForeColor = SystemColors.ControlText;
            }
        }

        sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => DarkSurface;
            public override Color ImageMarginGradientBegin => DarkSurface;
            public override Color ImageMarginGradientMiddle => DarkSurface;
            public override Color ImageMarginGradientEnd => DarkSurface;
            public override Color MenuBorder => DarkBorder;
            public override Color MenuItemBorder => DarkButtonHover;
            public override Color MenuItemSelected => DarkButtonHover;
            public override Color SeparatorDark => DarkBorder;
            public override Color SeparatorLight => DarkSurface;
        }

        // ---- Color helpers ----

        static Color Blend(Color a, Color b, double amountOfA) => Color.FromArgb(
            (int)(a.R * amountOfA + b.R * (1 - amountOfA)),
            (int)(a.G * amountOfA + b.G * (1 - amountOfA)),
            (int)(a.B * amountOfA + b.B * (1 - amountOfA)));

        // A custom text color (an accent) as it should be drawn in the current theme
        public static Color ForText(Color c) => IsDark ? Lighten(c) : c;

        // Dark custom text (an accent, a warning) made readable on a dark background
        static Color Lighten(Color c) => c.GetBrightness() < 0.55 ? Blend(c, Color.White, 0.45) : c;
    }
}

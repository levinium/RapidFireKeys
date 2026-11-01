using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using RapidFireKeys.Updates;
using RapidFireKeys.Views;

namespace RapidFireKeys
{
    public partial class Form1 : Form
    {
        static readonly string version = UpdateService.CurrentVersion;

        // Import user32.dll methods for interacting with Windows
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, int dwFelags, int dwExtraInfo);

        const int KEYEVENTF_KEYDOWN = 0x0000;
        const int KEYEVENTF_KEYUP = 0x0002;

        //---- New Send Mouse Click to Window Method -----
        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // Window message constants

        const uint WM_LBUTTONDOWN = 0x0201;
        const uint WM_LBUTTONUP = 0x0202;
        const uint WM_RBUTTONDOWN = 0x0204;
        const uint WM_RBUTTONUP = 0x0205;
        const uint WM_MBUTTONDOWN = 0x0207;
        const uint WM_MBUTTONUP = 0x0208;
        const uint WM_XBUTTONDOWN = 0x020B;
        const uint WM_XBUTTONUP = 0x020C;
        const int XBUTTON1 = 0x0001;
        const int XBUTTON2 = 0x0002;

        static async Task SendStealthMouseClick(Keys mouseButton, int holdMs)
        {
            //IntPtr hWnd = FindWindow(null, "Diablo II: Resurrected");
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return;

            uint downMsg = 0;
            uint upMsg = 0;
            IntPtr wParam = IntPtr.Zero;
            IntPtr lParam = IntPtr.Zero; // Use actual coordinates if needed

            switch (mouseButton)
            {
                case Keys.LButton:
                    downMsg = WM_LBUTTONDOWN;
                    upMsg = WM_LBUTTONUP;
                    wParam = (IntPtr)1; // MK_LBUTTON
                    break;
                case Keys.RButton:
                    downMsg = WM_RBUTTONDOWN;
                    upMsg = WM_RBUTTONUP;
                    wParam = (IntPtr)2; // MK_RBUTTON
                    break;
                case Keys.MButton:
                    downMsg = WM_MBUTTONDOWN;
                    upMsg = WM_MBUTTONUP;
                    wParam = (IntPtr)16; // MK_MBUTTON
                    break;
                case Keys.XButton1:
                    downMsg = WM_XBUTTONDOWN;
                    upMsg = WM_XBUTTONUP;
                    wParam = (IntPtr)(XBUTTON1 << 16 | 0x0001);
                    break;
                case Keys.XButton2:
                    downMsg = WM_XBUTTONDOWN;
                    upMsg = WM_XBUTTONUP;
                    wParam = (IntPtr)(XBUTTON2 << 16 | 0x0001);
                    break;
                default:
                    return; // Not a mouse button
            }

            PostMessage(hWnd, downMsg, wParam, lParam);
            await Task.Delay(holdMs);
            PostMessage(hWnd, upMsg, wParam, lParam);
        }

        //---------- Send Keyboard Press to Window Method -----------

        const uint WM_KEYDOWN = 0x0100;
        const uint WM_KEYUP = 0x0101;
        const uint WM_CHAR = 0x0102;
        const uint WM_SYSKEYDOWN = 0x0104; // For Alt+ combinations
        const uint WM_SYSKEYUP = 0x0105;

        static async Task SendStealthKeyPress(Keys key, int holdMs, bool extendedKey = false)
        {
            //IntPtr hWnd = FindWindow(null, "Diablo II: Resurrected");
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return;

            ushort scanCode = (ushort)MapVirtualKey((uint)key, 0);
            uint lParamDown, lParamUp;

            // Construct lParam values (bit-packed structure)
            lParamDown = 0x00000001 | (uint)(scanCode << 16);
            lParamUp = 0xC0000001 | (uint)(scanCode << 16);

            if (extendedKey)
            {
                lParamDown |= 0x01000000;
                lParamUp |= 0x01000000;
            }

            // Send messages. D2R samples key state once per frame, so a down and up
            // posted back-to-back land in the same frame and the press is never seen.
            // Hold the key long enough to span at least one frame.
            PostMessage(hWnd, WM_KEYDOWN, (IntPtr)key, (IntPtr)lParamDown);
            PostMessage(hWnd, WM_CHAR, (IntPtr)MapToChar(key), IntPtr.Zero);
            await Task.Delay(holdMs);
            PostMessage(hWnd, WM_KEYUP, (IntPtr)key, (IntPtr)lParamUp);
        }

        // Add this helper function
        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);

        // Simple character mapping (expand as needed)
        static char MapToChar(Keys key)
        {
            if (key >= Keys.A && key <= Keys.Z)
                return (char)('A' + (key - Keys.A));

            if (key >= Keys.D0 && key <= Keys.D9)
                return (char)('0' + (key - Keys.D0));

            return '\0'; // Handle special cases as needed
        }



        //-----------------------------

        // Data class to track key hold status
        class KeyState
        {
            public bool IsHeld = false;
            public DateTime HoldStart = DateTime.MinValue;
            public bool IsRepeating = false;
            public DateTime LastRepeat = DateTime.MinValue;
        }

        // The bindings the main loop is using, and the hold state of each key in them.
        // Swapped as one object when the config changes, so the loop never sees a new
        // map paired with an old set of key states.
        sealed class ActiveBindings
        {
            public readonly Dictionary<string, List<(Keys key, List<Keys> modifiers)>> Map;
            public readonly Dictionary<Keys, KeyState> States;

            public ActiveBindings(Dictionary<string, List<(Keys, List<Keys>)>> map, Dictionary<Keys, KeyState> states)
            {
                Map = map;
                States = states;
            }
        }

        static volatile ActiveBindings active = new(new(StringComparer.OrdinalIgnoreCase), new());

        static int holdThresholdMs = 100;
        static int repeatIntervalMs = 50;

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem updateNoticeItem;
        private ToolStripMenuItem checkUpdatesItem;

        private volatile bool disabled = false;

        private readonly Preferences preferences = Preferences.Load();

        // The last thing an update check concluded, so clicking the notice does not
        // have to ask again
        private UpdateVerdict? lastVerdict;

        // Tray apps stay running for days, so the daily check is re-tried on a timer
        // rather than only at startup
        private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 60 * 60 * 1000 };

        private SettingsForm? settingsForm;
        private UpdateForm? updateForm;

        public Form1()
        {
            InitializeComponent();
            Theme.Mode = preferences.Theme;
            Theme.Attach(this);
            InitializeTrayIcon();
            Log.Write($"RapidFireKeys v{version} started");
            LoadConfig();
            PhysicalInput.Start();
            StartBackgroundTask();

            _ = UpdateInstaller.CleanUpAfterUpdateAsync();
            updateTimer.Tick += (_, _) => CheckForUpdatesInBackground();
            updateTimer.Start();
            CheckForUpdatesInBackground();
        }

        private void InitializeTrayIcon()
        {
            trayMenu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("RapidFireKeys v"+version);
            titleItem.Enabled = false; // Make it non-clickable
            titleItem.Font = new Font("Segoe UI", 9F, FontStyle.Bold); // Make it look like a header

            // Hidden until an update check finds a newer release
            updateNoticeItem = new ToolStripMenuItem("Update available", null, (_, _) => ShowUpdateForm(lastVerdict))
            {
                Visible = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            };

            checkUpdatesItem = new ToolStripMenuItem("Check for Updates", null, (_, _) => CheckForUpdatesNow());

            trayMenu.Items.Add(titleItem);
            trayMenu.Items.Add(updateNoticeItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Disable", null, EnableDisableToggle);
            trayMenu.Items.Add(new ToolStripMenuItem("Settings...", null, (_, _) => OpenSettings()) { Font = new Font("Segoe UI", 9F, FontStyle.Bold) });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(checkUpdatesItem);

            // Only in builds published with a sponsor link; see SponsorLink
            if (SponsorLink.IsOffered)
                trayMenu.Items.Add("♥ Support RapidFireKeys", null, (_, _) => OpenSponsor(null));

            trayMenu.Items.Add("About", null, OnOpenClicked);
            trayMenu.Items.Add("Exit", null, OnExitClicked);

            trayIcon = new NotifyIcon()
            {
                //Icon = SystemIcons.Application, // You can use your own .ico file here
                Icon = LoadTrayIcon(),
                Text = "RapidFireKeys",
                ContextMenuStrip = trayMenu,
                Visible = true
            };

            Theme.ApplyMenu(trayMenu);
            Theme.Changed += () =>
            {
                if (trayMenu.InvokeRequired) trayMenu.BeginInvoke(() => Theme.ApplyMenu(trayMenu));
                else Theme.ApplyMenu(trayMenu);
            };

            trayIcon.MouseDoubleClick += TrayIcon_DoubleClick;
            trayIcon.BalloonTipClicked += (_, _) =>
            {
                if (lastVerdict is { IsAvailable: true })
                    ShowUpdateForm(lastVerdict);
            };
        }

        // The icon is embedded in the exe so the app runs as a single file
        internal static Icon LoadTrayIcon()
        {
            using var stream = typeof(Form1).Assembly.GetManifestResourceStream("rapidfire-icon.ico");
            return stream != null ? new Icon(stream) : SystemIcons.Application;
        }

        private void EnableDisableToggle(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem menuItem)
            {
                disabled = !disabled;
                menuItem.Text = disabled ? "Enable" : "Disable";
            }
        }

        private void OnOpenClicked(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            ExitApp();
        }

        private void ExitApp()
        {
            trayIcon.Visible = false;
            Application.Exit();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            versionLabel.Text = $"RapidFireKeys v{version}";
            this.Hide();
            this.ShowInTaskbar = false;
        }

        private void OpenSettings()
        {
            if (settingsForm is { IsDisposed: false })
            {
                settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Activate();
                return;
            }

            settingsForm = new SettingsForm(
                preferences,
                ApplyBindings,
                owner => CheckForUpdatesNow(owner),
                SponsorLink.IsOffered ? owner => OpenSponsor(owner) : null);
            settingsForm.FormClosed += (_, _) => settingsForm = null;
            settingsForm.Show();
            settingsForm.Activate();
        }

        private void OpenSponsor(IWin32Window? owner)
        {
            if (SponsorLink.Template is not { } template) return;

            using var form = new SponsorForm(template);
            form.ShowDialog(owner);
        }

        // Starts using the saved hotkeys. A settings file that could not be read
        // falls back to the defaults, and says so rather than failing silently.
        private void LoadConfig()
        {
            if (preferences.LoadError is { } error)
            {
                trayIcon.ShowBalloonTip(8000, "RapidFireKeys settings could not be read",
                    $"{error}\n\nThe default hotkeys are in use. Open Settings from the tray icon to set them up again.", ToolTipIcon.Warning);
            }

            ApplyBindings(preferences.Hotkeys);
        }

        private void ApplyBindings(Dictionary<string, List<KeyBinding>> bindings)
        {
            var map = HotkeyConfig.Compile(bindings, Log.Write);

            // Keys that stay bound keep their state, so a key being held right now
            // does not start a second repeat when the config is saved
            var previous = active.States;
            var states = new Dictionary<Keys, KeyState>();
            foreach (var process in map)
            {
                foreach (var (key, modifiers) in process.Value)
                {
                    if (!states.ContainsKey(key))
                        states[key] = previous.TryGetValue(key, out var state) ? state : new KeyState();
                }
            }

            active = new ActiveBindings(map, states);

            foreach (var (proc, keys) in map)
            {
                Log.Write($"Loaded {proc}: " + string.Join(", ", keys.Select(k =>
                    k.modifiers.Count > 0 ? $"{string.Join("+", k.modifiers)}+{k.key}" : k.key.ToString())));
            }
        }

        private void StartBackgroundTask()
        {
            Task.Run(() =>
            {
                // Main loop
                while (true)
                {
                    //Skip if disabled
                    if (disabled)
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    string activeProcess = GetForegroundProcessName();

                    var current = active;
                    current.Map.TryGetValue(activeProcess, out var procBindings);

                    if (procBindings != null)
                    {
                        foreach (var (key, modifiers) in procBindings)
                        {
                            bool isDown = IsKeyDown(key);
                            var state = current.States[key];

                            if (isDown)
                            {
                                if (!state.IsHeld)
                                {
                                    state.IsHeld = true;
                                    state.HoldStart = DateTime.Now;
                                }
                                else
                                {
                                    var heldMs = (DateTime.Now - state.HoldStart).TotalMilliseconds;
                                    if (heldMs > holdThresholdMs && !state.IsRepeating)
                                    {

                                        // Check if all modifiers are held down (if any exist), if they are not, do not start repeating
                                        if (!AreModifiersDown(modifiers))
                                        {
                                            state.IsHeld = false;
                                            state.IsRepeating = false;
                                            Thread.Sleep(10);
                                            continue;
                                        }

                                        state.IsRepeating = true;

                                        // Stop if the user switches windows, even to another instance of
                                        // the same game, so repeats never carry over into a different window
                                        IntPtr startWindow = GetForegroundWindow();
                                        Log.Write($"Repeat {key} started in {activeProcess} (window {startWindow})");
                                        _ = StartRepeatingKey(key, state, repeatIntervalMs, () =>
                                        {
                                            string? reason =
                                                !IsKeyDown(key) ? "released" :
                                                GetForegroundWindow() != startWindow ? "window changed" :
                                                !AreModifiersDown(modifiers) ? "modifier released" :
                                                disabled ? "disabled" :
                                                !IsStillBound(activeProcess, key) ? "removed from config" : null;
                                            if (reason != null)
                                                Log.Write($"Repeat {key} stopped: {reason}");
                                            return reason != null;
                                        });
                                    }
                                }
                            }
                            else
                            {
                                state.IsHeld = false;
                                state.IsRepeating = false;
                            }
                        }
                    }
                    else
                    {
                        // If not in target process, reset all key states
                        foreach (var state in current.States.Values)
                        {
                            state.IsHeld = false;
                            state.IsRepeating = false;
                        }
                    }

                    Thread.Sleep(10); // Keeps CPU usage low
                }
            });
        }

        // Whether a repeating key is still in the config, so saving Settings with a
        // key removed stops it even while it is held
        static bool IsStillBound(string processName, Keys key)
        {
            return active.Map.TryGetValue(processName, out var bindings)
                && bindings.Exists(b => b.key == key);
        }

        //---------- Updates -----------

        // Looks for a newer release, at most once a day, and says nothing unless it
        // finds one. Fire-and-forget: a failure is indistinguishable from "no update"
        // to the user, and rapid-fire must never wait on it.
        private async void CheckForUpdatesInBackground()
        {
            if (!UpdateService.IsAvailable) return;
            if (!preferences.CheckForUpdatesAutomatically) return;
            if (!UpdateSchedule.IsDue(preferences.LastUpdateCheckUtc, DateTimeOffset.UtcNow)) return;

            var verdict = await new UpdateService().CheckAsync();
            lastVerdict = verdict;

            // Only a completed check resets the clock. Recording the attempt when the
            // machine was offline would mean a PC started briefly each day never checks.
            if (verdict.Outcome != UpdateOutcome.Unknown)
            {
                preferences.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
                preferences.Save();
            }

            if (!verdict.IsAvailable) return;

            ShowUpdateNotice(verdict);
            Log.Write($"Version {verdict.Version} is available");
            trayIcon.ShowBalloonTip(10000, $"RapidFireKeys {verdict.Version} is available",
                "Click here to see what's new and update.", ToolTipIcon.Info);
        }

        private void ShowUpdateNotice(UpdateVerdict verdict)
        {
            updateNoticeItem.Text = $"Update to {verdict.Version}...";
            updateNoticeItem.Visible = true;
        }

        // Asks now, whatever the schedule says, and reports whatever it finds —
        // including "up to date", which is what someone who asked wants to hear
        private async void CheckForUpdatesNow(IWin32Window? owner = null)
        {
            if (!UpdateService.IsAvailable)
            {
                MessageBox.Show(owner, "This build was made without a release feed, so it cannot check for updates.",
                    "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            checkUpdatesItem.Enabled = false;
            try
            {
                var verdict = await new UpdateService().CheckAsync();
                lastVerdict = verdict;
                if (verdict.IsAvailable)
                    ShowUpdateNotice(verdict);
                ShowUpdateForm(verdict);
            }
            finally
            {
                checkUpdatesItem.Enabled = true;
            }
        }

        private void ShowUpdateForm(UpdateVerdict? verdict)
        {
            if (verdict is not { } v) return;

            updateForm?.Close();
            updateForm = new UpdateForm(v, ExitApp);
            updateForm.FormClosed += (_, _) => updateForm = null;
            updateForm.Show();
            updateForm.Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Intercept user-initiated close (like clicking the [X] button)
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // Cancel the close
                this.Hide();     // Hide the window instead
                this.ShowInTaskbar = false;
            }
            else
            {
                trayIcon.Visible = false; // Allow exit for system shutdown, app exit etc.
                base.OnFormClosing(e);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                this.ShowInTaskbar = false;
            }
        }



        public class MouseOperations
        {
            [Flags]
            public enum MouseEventFlags
            {
                LeftDown = 0x00000002,
                LeftUp = 0x00000004,
                MiddleDown = 0x00000020,
                MiddleUp = 0x00000040,
                Move = 0x00000001,
                Absolute = 0x00008000,
                RightDown = 0x00000008,
                RightUp = 0x00000010,
                XDown = 0x00000080,
                XUp = 0x00000100,
            }

            [DllImport("user32.dll", EntryPoint = "SetCursorPos")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool SetCursorPos(int x, int y);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool GetCursorPos(out MousePoint lpMousePoint);

            [DllImport("user32.dll")]
            private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

            public static void SetCursorPosition(int x, int y)
            {
                SetCursorPos(x, y);
            }

            public static void SetCursorPosition(MousePoint point)
            {
                SetCursorPos(point.X, point.Y);
            }

            public static MousePoint GetCursorPosition()
            {
                MousePoint currentMousePoint;
                var gotPoint = GetCursorPos(out currentMousePoint);
                if (!gotPoint) { currentMousePoint = new MousePoint(0, 0); }
                return currentMousePoint;
            }

            public static void MouseEvent(MouseEventFlags value)
            {
                MousePoint position = GetCursorPosition();

                mouse_event
                    ((int)value,
                     position.X,
                     position.Y,
                     0,
                     0);
            }

            public static void MouseEvent(MouseEventFlags value, int xButton)
            {
                MousePoint position = GetCursorPosition();

                mouse_event
                    ((int)value,
                     position.X,
                     position.Y,
                     xButton,
                     0);
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MousePoint
            {
                public int X;
                public int Y;

                public MousePoint(int x, int y)
                {
                    X = x;
                    Y = y;
                }
            }
        }

        // Keys Windows reports as down that the user is not physically holding,
        // tracked so each one is logged once rather than every poll
        static readonly HashSet<Keys> phantomKeys = new();

        // Checks if the key is currently down. Both sources must agree: GetAsyncKeyState
        // also counts keys pressed by other software and can be left stuck "down", which
        // made keys rapid-fire with nothing held; the hooks only see real input.
        static bool IsKeyDown(Keys key)
        {
            bool reported = (GetAsyncKeyState((int)key) & 0x8000) != 0;
            if (!PhysicalInput.Started)
                return reported;

            bool physical = PhysicalInput.IsDown((int)key);
            lock (phantomKeys)
            {
                if (reported && !physical)
                {
                    if (phantomKeys.Add(key))
                        Log.Write($"Ignoring {key}: Windows reports it held but it is not physically pressed");
                }
                else if (phantomKeys.Remove(key))
                {
                    Log.Write($"{key} no longer reported as phantom-held");
                }
            }
            return reported && physical;
        }

        // Checks if any of the modifier keys are currently down
        static bool AreModifiersDown(List<Keys> modifiers)
        {
            if (modifiers == null || modifiers.Count == 0)
                return true;

            foreach (var mod in modifiers)
            {
                if (!IsKeyDown(mod))
                    return false;
            }

            return true;
        }

        // Gets the name of the currently focused process
        static string GetForegroundProcessName()
        {
            IntPtr hwnd = GetForegroundWindow();
            GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                return Process.GetProcessById((int)pid).ProcessName + ".exe";
            }
            catch
            {
                return string.Empty;
            }
        }

        // Continuously sends key input while the key is still down and the process is in focus
        static async Task StartRepeatingKey(Keys key, KeyState state, int intervalMs, Func<bool> stopCondition)
        {
            await Task.Delay(holdThresholdMs);
            while (!stopCondition())
            {
                // Split each interval evenly between holding the key down and releasing
                // it, so the game sees both states and the repeat rate stays the same.
                int holdMs = intervalMs / 2;
                await SendKey(key, holdMs);
                state.LastRepeat = DateTime.Now;
                await Task.Delay(intervalMs - holdMs);
            }
        }

        // Sends a single press of the given key
        static async Task SendKey(Keys key, int holdMs)
        {
            if (IsMouseKey(key))
            {
                await SendStealthMouseClick(key, holdMs);
            }
            else
            {
                await SendStealthKeyPress(key, holdMs);
            }
        }

        static bool IsMouseKey(Keys key)
        {
            return key == Keys.LButton || key == Keys.RButton || key == Keys.MButton || key == Keys.XButton1 || key == Keys.XButton2;
        }

        private void TrayIcon_DoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                OpenSettings();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }
    }
}

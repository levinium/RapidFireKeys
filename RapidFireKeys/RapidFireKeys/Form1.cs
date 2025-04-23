using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms.VisualStyles;

namespace RapidFireKeys
{
    public partial class Form1 : Form
    {
        static String version = "1.0.1";

        // Import user32.dll methods for interacting with Windows
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, int dwFelags, int dwExtraInfo);

        const int KEYEVENTF_KEYDOWN = 0x0000;
        const int KEYEVENTF_KEYUP = 0x0002;

        // Data class to track key hold status
        class KeyState
        {
            public bool IsHeld = false;
            public DateTime HoldStart = DateTime.MinValue;
            public bool IsRepeating = false;
        }

        // All standard virtual key codes (partial, expand as needed)
        enum Keys
        {
            LButton = 0x01, RButton = 0x02, MButton = 0x04,
            XButton1 = 0x05, XButton2 = 0x06,
            Back = 0x08, Tab = 0x09,
            Enter = 0x0D, Shift = 0x10, Ctrl = 0x11, Alt = 0x12,
            Pause = 0x13, CapsLock = 0x14, Esc = 0x1B,
            Space = 0x20, PageUp = 0x21, PageDown = 0x22, End = 0x23, Home = 0x24,
            Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28,
            Insert = 0x2D, Delete = 0x2E,
            D0 = 0x30, D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34,
            D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38, D9 = 0x39,
            A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45,
            F = 0x46, G = 0x47, H = 0x48, I = 0x49, J = 0x4A,
            K = 0x4B, L = 0x4C, M = 0x4D, N = 0x4E, O = 0x4F,
            P = 0x50, Q = 0x51, R = 0x52, S = 0x53, T = 0x54,
            U = 0x55, V = 0x56, W = 0x57, X = 0x58, Y = 0x59, Z = 0x5A,
            LWin = 0x5B, RWin = 0x5C, Apps = 0x5D,
            Numpad0 = 0x60, Numpad1 = 0x61, Numpad2 = 0x62, Numpad3 = 0x63,
            Numpad4 = 0x64, Numpad5 = 0x65, Numpad6 = 0x66, Numpad7 = 0x67,
            Numpad8 = 0x68, Numpad9 = 0x69,
            Multiply = 0x6A, Add = 0x6B, Subtract = 0x6D, Decimal = 0x6E, Divide = 0x6F,
            F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74,
            F6 = 0x75, F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79,
            F11 = 0x7A, F12 = 0x7B,
            NumLock = 0x90, ScrollLock = 0x91,
        }

        static Dictionary<Keys, KeyState> keyStates = new();

        static int holdThresholdMs = 100;
        static int repeatIntervalMs = 50;

        static Dictionary<string, List<(Keys key, List<Keys> modifiers)>> finalMap
            = new Dictionary<string, List<(Keys, List<Keys>)>>();

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        private bool disabled = false;

        public Form1()
        {
            InitializeComponent();
            InitializeTrayIcon();
            StartBackgroundTask();
        }

        private void InitializeTrayIcon()
        {
            trayMenu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("RapidFireKeys v"+version);
            titleItem.Enabled = false; // Make it non-clickable
            titleItem.Font = new Font("Segoe UI", 9F, FontStyle.Bold); // Make it look like a header
            
            trayMenu.Items.Add(titleItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Disable", null, EnableDisableToggle);
            trayMenu.Items.Add("Reload CONFIG.json", null, LoadConfig);
            trayMenu.Items.Add("About", null, OnOpenClicked);
            trayMenu.Items.Add("Exit", null, OnExitClicked);

            trayIcon = new NotifyIcon()
            {
                //Icon = SystemIcons.Application, // You can use your own .ico file here
                Icon = new Icon("rapidfire-icon.ico"),
                Text = "RapidFireKeys",
                ContextMenuStrip = trayMenu,
                Visible = true
            };

            trayIcon.MouseDoubleClick += TrayIcon_DoubleClick;
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

        private void LoadConfig(object sender, EventArgs e)
        {
            LoadConfig();
        }

        private void LoadConfig()
        {
            string jsonPath = "CONFIG.json";

            if (!File.Exists(jsonPath))
            {
                Console.WriteLine("JSON file not found.");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var bindings = JsonSerializer.Deserialize<Dictionary<string, List<KeyBinding>>>(json);

            foreach (var process in bindings)
            {
                var keyList = new List<(Keys, List<Keys>)>();

                foreach (var bind in process.Value)
                {
                    //Console.WriteLine($"Trying to parse key: '{NormalizeKeyName(bind.Key)}'");
                    if (Enum.TryParse<Keys>(NormalizeKeyName(bind.Key), true, out Keys mainKey))
                    {
                        List<Keys> modifierKeys = new();
                        if (bind.Modifiers != null)
                        {
                            foreach (var mod in bind.Modifiers)
                            {
                                if (Enum.TryParse<Keys>(NormalizeKeyName(mod), true, out Keys modKey))
                                {
                                    modifierKeys.Add(modKey);
                                }
                                else
                                {
                                    Console.WriteLine($"Invalid modifier: {mod}");
                                }
                            }
                        }

                        keyList.Add((mainKey, modifierKeys));
                    }
                    else
                    {
                        Console.WriteLine($"Invalid key: {bind.Key}");
                    }
                }

                finalMap[process.Key] = keyList;
            }

            // 🧪 Print for testing
            foreach (var (proc, keys) in finalMap)
            {
                Console.WriteLine($"\nProcess: {proc}");
                foreach (var (key, mods) in keys)
                {
                    Console.WriteLine($"  Key: {key}, Modifiers: {(mods.Count > 0 ? string.Join("+", mods) : "None")}");
                }
            }



            // Initialize key states
            //foreach (var key in keysToMonitor)
            //    keyStates[key] = new KeyState();

            //Initialize key states from the JSON
            foreach (var process in finalMap)
            {
                foreach (var (key, modifiers) in process.Value)
                {
                    if (!keyStates.ContainsKey(key))
                    {
                        keyStates[key] = new KeyState();
                    }
                }
            }
        }

        private void StartBackgroundTask()
        {
            Task.Run(() =>
            {
                LoadConfig();

                Console.WriteLine("\n\nMonitoring and rapidly repeat-pressing held keys while target processes are in focus.\n\nThis console window must remain open for the program to continue running.");

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

                    var procBindings = GetKeyBindingsForProcess(activeProcess);

                    if (procBindings != null)
                    {
                        foreach (var (key, modifiers) in procBindings)
                        {
                            bool isDown = IsKeyDown(key);
                            var state = keyStates[key];

                            if (isDown)
                            {
                                if (!state.IsHeld)
                                {
                                    //Console.WriteLine($"Key {key} is held");
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
                                        _ = StartRepeatingKey(key, repeatIntervalMs, () => !IsKeyDown(key) || GetKeyBindingsForProcess(GetForegroundProcessName()) == null || !AreModifiersDown(modifiers));
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
                        foreach (var state in keyStates.Values)
                        {
                            state.IsHeld = false;
                            state.IsRepeating = false;
                        }
                    }

                    Thread.Sleep(10); // Keeps CPU usage low
                }
            });
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

        // Checks if the key is currently down
        static bool IsKeyDown(Keys key)
        {
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
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
        static async Task StartRepeatingKey(Keys key, int intervalMs, Func<bool> stopCondition)
        {
            await Task.Delay(holdThresholdMs);
            while (!stopCondition())
            {
                SendKey(key);
                await Task.Delay(intervalMs);
            }
        }

        // Sends a single press of the given key
        static void SendKey(Keys key)
        {
            if (IsMouseKey(key))
            {
                SendMouseClick(key);
            }
            else
            {
                SendKeyboardPress(key);
            }
        }

        static void SendMouseClick(Keys mouseButton)
        {
            MouseOperations.MouseEventFlags downFlag, upFlag;

            int xButton = 0;

            switch (mouseButton)
            {
                case Keys.LButton:
                    downFlag = MouseOperations.MouseEventFlags.LeftDown;
                    upFlag = MouseOperations.MouseEventFlags.LeftUp;
                    break;
                case Keys.RButton:
                    downFlag = MouseOperations.MouseEventFlags.RightDown;
                    upFlag = MouseOperations.MouseEventFlags.RightUp;
                    break;
                case Keys.MButton:
                    downFlag = MouseOperations.MouseEventFlags.MiddleDown;
                    upFlag = MouseOperations.MouseEventFlags.MiddleUp;
                    break;
                case Keys.XButton1:
                    downFlag = MouseOperations.MouseEventFlags.XDown;
                    upFlag = MouseOperations.MouseEventFlags.XUp;
                    xButton = 1;
                    break;
                case Keys.XButton2:
                    downFlag = MouseOperations.MouseEventFlags.XDown;
                    upFlag = MouseOperations.MouseEventFlags.XUp;
                    xButton = 2;
                    break;
                default:
                    Console.WriteLine($"Unsupported mouse button: {mouseButton}");
                    return;
            }

            MouseOperations.MouseEvent(downFlag, xButton);
        }

        static void SendKeyboardPress(Keys key)
        {
            //Check if it is a valid key
            if (!Enum.IsDefined(typeof(Keys), key))
            {
                Console.WriteLine($"Unsupported keyboard key: {key}");
                return;
            }
            // Regular keyboard input
            keybd_event((byte)key, 0, KEYEVENTF_KEYDOWN, 0);
            keybd_event((byte)key, 0, KEYEVENTF_KEYUP, 0);
            Thread.Sleep(repeatIntervalMs);
            keybd_event((byte)key, 0, KEYEVENTF_KEYDOWN, 0);
            keybd_event((byte)key, 0, KEYEVENTF_KEYUP, 0);
        }

        static bool IsMouseKey(Keys key)
        {
            return key == Keys.LButton || key == Keys.RButton || key == Keys.MButton || key == Keys.XButton1 || key == Keys.XButton2;
        }

        static List<(Keys key, List<Keys> modifiers)>? GetKeyBindingsForProcess(string processName)
        {
            if (finalMap.TryGetValue(processName, out var bindings))
            {
                return bindings;
            }

            return null;
        }


        static string NormalizeKeyName(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return "";

            keyName = keyName.Trim(); // ← Strip any leading/trailing whitespace

            // Normalize digits ("1" → "D1") but ignore already-normal keys
            return (keyName.Length == 1 && char.IsDigit(keyName[0]))
                ? $"D{keyName}"
                : keyName;
        }

        class KeyBinding
        {
            [JsonPropertyName("key")]
            public string Key { get; set; }

            [JsonPropertyName("modifiers")]
            public List<string> Modifiers { get; set; }
        }

        private void TrayIcon_DoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.ShowInTaskbar = true;
            }
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

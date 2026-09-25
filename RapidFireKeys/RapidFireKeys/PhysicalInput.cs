using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace RapidFireKeys
{
    // Tracks which keys and mouse buttons are physically held, using low-level hooks.
    //
    // GetAsyncKeyState alone also reports keys pressed by software (other tools'
    // SendInput/mouse_event, macro software), and can be left reporting "down" if
    // a key-up is never recorded. Either one made RapidFireKeys start firing with
    // nothing held. The hooks see every real event and skip injected ones.
    static class PhysicalInput
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string? lpModuleName);

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public UIntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT { public int ptX; public int ptY; public uint mouseData; public uint flags; public uint time; public UIntPtr dwExtraInfo; }

        const int WH_KEYBOARD_LL = 13;
        const int WH_MOUSE_LL = 14;
        const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
        const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
        const int WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
        const uint LLKHF_INJECTED = 0x10;
        const uint LLMHF_INJECTED = 0x01;

        // Indexed by virtual key code
        static readonly bool[] down = new bool[256];

        // Held in fields so the GC never collects the delegates while Windows holds them
        static HookProc? keyboardProc;
        static HookProc? mouseProc;

        public static bool Started { get; private set; }

        public static void Start()
        {
            // The hooks get their own thread and message loop, so a busy UI thread can
            // never delay a callback past Windows' hook timeout (which silently removes it).
            var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                keyboardProc = KeyboardHook;
                mouseProc = MouseHook;
                IntPtr module = GetModuleHandle(null);
                bool ok = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0) != IntPtr.Zero
                        & SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0) != IntPtr.Zero;
                Started = ok;
                Log.Write(ok ? "Input hooks installed" : $"ERROR: input hooks failed (error {Marshal.GetLastWin32Error()})");
                ready.Set();

                while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            })
            {
                IsBackground = true,
                Name = "PhysicalInput hooks",
                Priority = ThreadPriority.AboveNormal
            };
            thread.Start();
            ready.Wait(2000);
        }

        public static bool IsDown(int vk)
        {
            return vk switch
            {
                // Hooks report left/right modifiers; config uses the generic ones
                0x10 => Volatile.Read(ref down[0xA0]) || Volatile.Read(ref down[0xA1]), // Shift
                0x11 => Volatile.Read(ref down[0xA2]) || Volatile.Read(ref down[0xA3]), // Ctrl
                0x12 => Volatile.Read(ref down[0xA4]) || Volatile.Read(ref down[0xA5]), // Alt
                _ => vk is >= 0 and < 256 && Volatile.Read(ref down[vk]),
            };
        }

        static IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((data.flags & LLKHF_INJECTED) == 0 && data.vkCode < 256)
                {
                    int msg = (int)wParam;
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                        Volatile.Write(ref down[data.vkCode], true);
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                        Volatile.Write(ref down[data.vkCode], false);
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        static IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if ((data.flags & LLMHF_INJECTED) == 0)
                {
                    switch ((int)wParam)
                    {
                        case WM_LBUTTONDOWN: Volatile.Write(ref down[0x01], true); break;
                        case WM_LBUTTONUP: Volatile.Write(ref down[0x01], false); break;
                        case WM_RBUTTONDOWN: Volatile.Write(ref down[0x02], true); break;
                        case WM_RBUTTONUP: Volatile.Write(ref down[0x02], false); break;
                        case WM_MBUTTONDOWN: Volatile.Write(ref down[0x04], true); break;
                        case WM_MBUTTONUP: Volatile.Write(ref down[0x04], false); break;
                        case WM_XBUTTONDOWN:
                        case WM_XBUTTONUP:
                            // High word of mouseData says which X button: 1 -> VK 0x05, 2 -> VK 0x06
                            int vk = (data.mouseData >> 16) == 1 ? 0x05 : 0x06;
                            Volatile.Write(ref down[vk], (int)wParam == WM_XBUTTONDOWN);
                            break;
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
    }

    // Small log next to the exe, so a misfire can be traced after the fact
    static class Log
    {
        static readonly string path = Path.Combine(AppContext.BaseDirectory, "RapidFireKeys.log");
        static readonly object sync = new();

        static Log()
        {
            try
            {
                // Start fresh once the log gets large
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                    File.Delete(path);
            }
            catch { }
        }

        public static void Write(string message)
        {
            try
            {
                lock (sync)
                    File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}

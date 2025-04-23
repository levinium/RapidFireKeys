# RapidFireKeys
System tray application built in C# to rapidly fire specific keys for specific applications when the keys are held down.

### 🔽 Pre-Compiled .zip Download

[Download RapidFireKeys v1.0.0](https://github.com/YOUR_USERNAME/RapidFireKeys/releases/latest/download/RapidFireKeys.zip)


Note: RapidFireKeys launches in the system tray and can be managed from there.


GUIDE:

To customize what applications RapidFireKeys works for and what hotkeys are rapid-fired, edit the CONFIG.json file located in the RapidFireKeys.exe directory.

Here is an example CONFIG.json file that runs for D2R.exe (Diablo II Resurrected game), and "OtherApp.exe" (some other application)

A key that has "modifiers" defined will only rapid-fire while those modifier keys are also pressed; for example in D2R.exe, Left Mouse Button will only rapid-fire while Shift is also held down.

Example CONFIG.json:
```
{
    "D2R.exe": [
        { "key": "LButton", "modifiers": ["Shift"] },
        { "key": "RButton" },
        { "key": "MButton"},
        { "key": "XButton1"},
        { "key": "XButton2"},
        { "key": "Back"},
        { "key": "Q" },
        { "key": "W" },
        { "key": "E" },
        { "key": "R" },
        { "key": "A" },
        { "key": "S" },
        { "key": "D" },
        { "key": "F" },
        { "key": "G" },
        { "key": "H" },
        { "key": "B" }
    ],
    "OtherApp.exe": [
       { "key": "LButton", "modifiers": ["Shift", "Ctrl", "Alt"] },
       { "key": "RButton" },
    ]
}
```


List of possible keys that can be rapid-fired (not every key is fully tested):
```
LButton, RButton, MButton, XButton1, XButton2, Back,
Tab, Enter, Shift, Ctrl, Alt, Pause, CapsLock, Esc, Space, PageUp, PageDown, End, Home, Left, Up, Right, Down, Insert, Delete,
D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
LWin, RWin, Apps, Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
```

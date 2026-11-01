# Changelog

## 1.1.0

- **Added a Settings window.** Double-click the tray icon (or choose
  **Settings...** from its menu) to add and remove apps and hotkeys. Add a
  hotkey by pressing the key or clicking the mouse button, and tick Shift, Ctrl
  or Alt to require a modifier. Saving takes effect immediately. CONFIG.json is
  still used and can still be edited by hand; the defaults are unchanged.
- **Added automatic updates.** RapidFireKeys checks for a new release once a
  day, shows the release notes, and can download, verify and install it with
  one click, restarting itself afterwards. The check can be turned off in
  Settings, and **Check for Updates** is in the tray menu.
- **Added dark mode.** Windows and the tray menu follow Windows' light/dark
  app setting by default, including when it changes while running; **Theme** in
  Settings can force Light or Dark.
- **Added a Support RapidFireKeys option** to the tray menu and Settings in
  official release builds.
- A CONFIG.json with a trailing comma or comments is now read instead of
  silently stopping all rapid-fire. If CONFIG.json cannot be read, the tray icon
  now says so and the previous hotkeys stay in effect.
- App names in CONFIG.json now match regardless of capitalization.
- Reloading CONFIG.json now drops apps that were removed from it, and a key that
  is removed while held stops firing.

Upgrading: replace RapidFireKeys.exe with this one. Your CONFIG.json is kept.
From here on, updates can be installed from inside the app.

## 1.0.5

- Fixed keys rapid-firing by themselves with nothing held. RapidFireKeys now
  watches the physical keyboard and mouse and ignores input sent by other
  software.
- Rapid-fire stops as soon as you switch to a different window, including
  another instance of the same game.
- Added RapidFireKeys.log next to the exe, recording when each rapid-fire starts
  and stops.

## 1.0.4

- Fixed rapid-fire not working after a Diablo II: Resurrected update that
  samples key state once per frame. Each press is now held briefly so the game
  sees it, at the same repeat rate.
- RapidFireKeys is now a single self-contained RapidFireKeys.exe.

## 1.0.3

- RapidFireKeys always launches with administrator privileges, so rapid-fired
  keys reach applications that are themselves running elevated.

## 1.0.2

- Keys are sent directly to the foreground window, fixing an occasional missed
  mouse button release.

## 1.0.1

- Fixed support for mouse XButton1 and XButton2.
- Fixed a minor CPU usage issue with keys that have modifiers.

## 1.0.0

- First release, set up for Diablo II: Resurrected (D2R.exe).

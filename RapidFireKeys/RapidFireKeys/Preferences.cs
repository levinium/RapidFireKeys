using System.Text.Json;
using System.Text.Json.Serialization;

namespace RapidFireKeys
{
    // Everything RapidFireKeys remembers: which keys rapid-fire in which apps, plus
    // the app's own settings. Kept in one file under %AppData%\RapidFireKeys, so
    // the folder the exe lives in stays just the exe, and the Settings window is
    // the one place to change any of it. Export/Import in Settings copy this file
    // to and from anywhere.
    internal sealed class Preferences
    {
        public static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RapidFireKeys");

        static string FilePath => Path.Combine(Folder, "settings.json");

        // Before 1.1.0 these sat next to the exe. Read once, on the first run that
        // finds no settings in AppData, and left where they are.
        static readonly string legacyConfigPath = Path.Combine(AppContext.BaseDirectory, "CONFIG.json");
        static readonly string legacyPreferencesPath = Path.Combine(AppContext.BaseDirectory, "RapidFireKeys.settings.json");

        static readonly JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // Which keys rapid-fire in which apps, keyed by the app's .exe name. The
        // same shape CONFIG.json always had, so an old CONFIG.json imports as is.
        public Dictionary<string, List<KeyBinding>> Hotkeys { get; set; } = HotkeyConfig.Defaults();

        // Light or dark windows. System follows Windows' own app mode, including
        // when it changes while RapidFireKeys is running.
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ThemeMode Theme { get; set; } = ThemeMode.System;

        // Look for a newer release in the background, at most once a day. The check
        // sends nothing about the machine; it is a plain request for a public file.
        public bool CheckForUpdatesAutomatically { get; set; } = true;

        // When the last automatic check completed, so the next one waits a day.
        // Left untouched by a manual check, and never exported.
        public DateTimeOffset? LastUpdateCheckUtc { get; set; }

        // Why the settings file could not be read at startup, if it could not.
        // The defaults are used instead and the Settings window says so.
        [JsonIgnore]
        public string? LoadError { get; private set; }

        public static Preferences Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    return Parse(File.ReadAllText(FilePath));
                }
                catch (Exception ex)
                {
                    // Kept aside rather than overwritten by the next save, in case
                    // it is worth recovering by hand
                    try { File.Copy(FilePath, FilePath + ".unreadable", overwrite: true); } catch { }
                    Log.Write($"ERROR: could not read {FilePath}, using defaults: {ex.Message}");
                    return new Preferences { LoadError = ex.Message };
                }
            }

            var migrated = MigrateLegacy();
            migrated.Save();
            return migrated;
        }

        static Preferences MigrateLegacy()
        {
            var prefs = new Preferences();

            try
            {
                if (File.Exists(legacyPreferencesPath))
                {
                    var old = Parse(File.ReadAllText(legacyPreferencesPath));
                    prefs.Theme = old.Theme;
                    prefs.CheckForUpdatesAutomatically = old.CheckForUpdatesAutomatically;
                    prefs.LastUpdateCheckUtc = old.LastUpdateCheckUtc;
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Could not read {legacyPreferencesPath}: {ex.Message}");
            }

            try
            {
                if (File.Exists(legacyConfigPath))
                {
                    prefs.Hotkeys = HotkeyConfig.Parse(File.ReadAllText(legacyConfigPath));
                    Log.Write($"Imported hotkeys from {legacyConfigPath}; it is no longer used");
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Could not import {legacyConfigPath}, using the default hotkeys: {ex.Message}");
            }

            return prefs;
        }

        // Throws when the text is not a settings file
        public static Preferences Parse(string json)
        {
            var prefs = JsonSerializer.Deserialize<Preferences>(json, jsonOptions)
                ?? throw new JsonException("The settings file is empty.");
            prefs.Hotkeys = HotkeyConfig.Clean(prefs.Hotkeys);
            return prefs;
        }

        public string Serialize() => JsonSerializer.Serialize(this, jsonOptions);

        // Writes through a temporary file, so a failed save never leaves a
        // half-written settings file behind. Returns why it failed, or null.
        public string? Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, Serialize());
                File.Move(temp, FilePath, overwrite: true);
                return null;
            }
            catch (Exception ex)
            {
                Log.Write($"Could not save {FilePath}: {ex.Message}");
                return ex.Message;
            }
        }

        // What Export writes: everything but the update-check clock, which is about
        // this machine rather than anyone's setup
        public static string Export(Dictionary<string, List<KeyBinding>> hotkeys, ThemeMode theme, bool checkForUpdates) =>
            new Preferences { Hotkeys = hotkeys, Theme = theme, CheckForUpdatesAutomatically = checkForUpdates }.Serialize();

        // Reads a file someone chose to import: an exported settings file, or a
        // CONFIG.json from before 1.1.0. HasPreferences is false for the latter,
        // which carries hotkeys only.
        public static (Preferences Settings, bool HasPreferences) Import(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("This is not a RapidFireKeys settings file.");

            bool isExport = doc.RootElement.EnumerateObject()
                .Any(p => string.Equals(p.Name, "hotkeys", StringComparison.OrdinalIgnoreCase));
            if (isExport)
                return (Parse(json), true);

            // An old CONFIG.json: every entry is an app name holding a list of keys
            bool isLegacy = doc.RootElement.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.Array);
            if (!isLegacy)
                throw new JsonException("This is not a RapidFireKeys settings file.");

            return (new Preferences { Hotkeys = HotkeyConfig.Parse(json) }, false);
        }
    }
}

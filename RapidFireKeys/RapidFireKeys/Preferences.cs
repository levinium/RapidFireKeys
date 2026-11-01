using System.Text.Json;

namespace RapidFireKeys
{
    // App settings that are not hotkeys, kept next to the exe like CONFIG.json so
    // the whole app stays in one folder.
    //
    // Separate from CONFIG.json because older versions read every top-level entry
    // there as an app name; a settings entry would make them fail to load it.
    internal sealed class Preferences
    {
        static readonly string filePath = Path.Combine(AppContext.BaseDirectory, "RapidFireKeys.settings.json");

        static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

        // Look for a newer release in the background, at most once a day. The check
        // sends nothing about the machine; it is a plain request for a public file.
        public bool CheckForUpdatesAutomatically { get; set; } = true;

        // When the last automatic check completed, so the next one waits a day.
        // Left untouched by a manual check.
        public DateTimeOffset? LastUpdateCheckUtc { get; set; }

        // Light or dark windows. System follows Windows' own app mode, including
        // when it changes while RapidFireKeys is running.
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public ThemeMode Theme { get; set; } = ThemeMode.System;

        public static Preferences Load()
        {
            try
            {
                if (File.Exists(filePath))
                    return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(filePath), jsonOptions) ?? new();
            }
            catch (Exception ex)
            {
                Log.Write($"Could not read {filePath}, using defaults: {ex.Message}");
            }
            return new();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(filePath, JsonSerializer.Serialize(this, jsonOptions));
            }
            catch (Exception ex)
            {
                Log.Write($"Could not save {filePath}: {ex.Message}");
            }
        }
    }
}

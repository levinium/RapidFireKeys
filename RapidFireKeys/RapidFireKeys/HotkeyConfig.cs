using System.Text.Json;
using System.Text.Json.Serialization;

namespace RapidFireKeys
{
    // One key to rapid-fire, as saved in the settings file
    internal sealed class KeyBinding
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = "";

        [JsonPropertyName("modifiers")]
        public List<string>? Modifiers { get; set; }
    }

    // Which keys rapid-fire in which apps, in the shape CONFIG.json had before
    // 1.1.0 and the settings file's "hotkeys" entry has now: app .exe name to a
    // list of keys. Reading stays lenient, since old configs were written by hand.
    internal static class HotkeyConfig
    {
        public const string DefaultJson = """
            {
                "D2R.exe": [
                    { "key": "LButton", "modifiers": ["Shift"] },
                    { "key": "RButton" },
                    { "key": "MButton" },
                    { "key": "XButton1" },
                    { "key": "XButton2" },
                    { "key": "Back" },
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
                ]
            }
            """;

        // Hand-written configs are forgiving: the README's own example once had a
        // trailing comma, and a comment is a natural thing to leave in a config.
        static readonly JsonSerializerOptions readOptions = new()
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
        };

        public static Dictionary<string, List<KeyBinding>> Defaults() => Parse(DefaultJson);

        // Throws JsonException when the text is not a valid config
        public static Dictionary<string, List<KeyBinding>> Parse(string json)
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, List<KeyBinding>?>>(json, readOptions)
                ?? throw new JsonException("The hotkey list is empty.");
            return Clean(parsed);
        }

        // Drops the nulls a hand-written file can contain ("D2R.exe": null, or a
        // stray null in a key list), so nothing downstream has to expect them
        public static Dictionary<string, List<KeyBinding>> Clean(Dictionary<string, List<KeyBinding>?>? bindings)
        {
            var result = new Dictionary<string, List<KeyBinding>>();
            foreach (var (process, list) in bindings ?? new())
                result[process] = list?.Where(b => b != null).ToList() ?? new();
            return result;
        }

        // Turns the config into what the main loop checks. Process names match
        // regardless of case, since Windows file names do too.
        public static Dictionary<string, List<(Keys key, List<Keys> modifiers)>> Compile(
            Dictionary<string, List<KeyBinding>> bindings, Action<string> log)
        {
            var map = new Dictionary<string, List<(Keys, List<Keys>)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (process, list) in bindings)
            {
                var keyList = new List<(Keys, List<Keys>)>();

                foreach (var bind in list)
                {
                    if (!TryParseKey(bind.Key, out Keys mainKey))
                    {
                        log($"Invalid key for {process}: {bind.Key}");
                        continue;
                    }

                    List<Keys> modifierKeys = new();
                    foreach (var mod in bind.Modifiers ?? new())
                    {
                        if (TryParseKey(mod, out Keys modKey))
                            modifierKeys.Add(modKey);
                        else
                            log($"Invalid modifier for {process}: {mod}");
                    }

                    keyList.Add((mainKey, modifierKeys));
                }

                map[process] = keyList;
            }

            return map;
        }

        // Accepts the names in the Keys enum, case-insensitively, and a bare digit
        // ("1") for the matching number key. Numbers that are not names ("65") are
        // refused, though Enum.TryParse alone would accept them.
        public static bool TryParseKey(string? name, out Keys key)
        {
            key = default;
            string normalized = NormalizeKeyName(name);
            if (normalized.Length == 0 || char.IsDigit(normalized[0]))
                return false;

            return Enum.TryParse(normalized, true, out key) && Enum.IsDefined(key);
        }

        static string NormalizeKeyName(string? keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return "";

            keyName = keyName.Trim(); // ← Strip any leading/trailing whitespace

            // Normalize digits ("1" → "D1") but ignore already-normal keys
            return (keyName.Length == 1 && char.IsDigit(keyName[0]))
                ? $"D{keyName}"
                : keyName;
        }

        // "notepad" and "C:\Games\D2R.exe" both become what the main loop compares
        // against: the bare file name, with .exe on the end
        public static string NormalizeProcessName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            string trimmed = Path.GetFileName(name.Trim().Trim('"'));
            return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed + ".exe";
        }
    }
}

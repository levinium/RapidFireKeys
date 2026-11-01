using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RapidFireKeys
{
    // One key to rapid-fire, as written in CONFIG.json
    internal sealed class KeyBinding
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = "";

        [JsonPropertyName("modifiers")]
        public List<string>? Modifiers { get; set; }
    }

    // CONFIG.json: which keys rapid-fire in which apps.
    //
    // The Settings window edits this file rather than replacing it with a new format,
    // so a config written by hand for an older version keeps working, and anyone who
    // still prefers editing it by hand can.
    internal static class HotkeyConfig
    {
        // CONFIG.json lives next to the exe. Written out on first run if missing.
        public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "CONFIG.json");

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

        // Hand-edited configs are forgiving: the README's own example once had a
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
                ?? throw new JsonException("CONFIG.json is empty.");

            var result = new Dictionary<string, List<KeyBinding>>();
            foreach (var (process, bindings) in parsed)
                result[process] = bindings?.Where(b => b != null).ToList() ?? new();
            return result;
        }

        // Reads CONFIG.json, creating it with the defaults on first run.
        // Error is null on success; on failure Bindings is empty and Error says why.
        public static (Dictionary<string, List<KeyBinding>> Bindings, string? Error) Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    File.WriteAllText(FilePath, DefaultJson);

                return (Parse(File.ReadAllText(FilePath)), null);
            }
            catch (Exception ex)
            {
                return (new(), ex.Message);
            }
        }

        // Writes to a temporary file first, so a failed save never leaves a
        // half-written CONFIG.json behind
        public static void Save(Dictionary<string, List<KeyBinding>> bindings)
        {
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, Serialize(bindings));
            File.Move(temp, FilePath, overwrite: true);
        }

        // Laid out one binding per line, like the default, so the file stays easy
        // to read and edit by hand
        public static string Serialize(Dictionary<string, List<KeyBinding>> bindings)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");

            int processIndex = 0;
            foreach (var (process, list) in bindings)
            {
                sb.Append($"    {Quote(process)}: [");
                if (list.Count > 0)
                    sb.Append('\n');

                for (int i = 0; i < list.Count; i++)
                {
                    var bind = list[i];
                    sb.Append($"        {{ \"key\": {Quote(bind.Key)}");
                    if (bind.Modifiers is { Count: > 0 } mods)
                        sb.Append($", \"modifiers\": [{string.Join(", ", mods.Select(Quote))}]");
                    sb.Append(" }");
                    sb.Append(i < list.Count - 1 ? ",\n" : "\n");
                }

                sb.Append(list.Count > 0 ? "    ]" : "]");
                sb.Append(++processIndex < bindings.Count ? ",\n" : "\n");
            }

            sb.Append("}\n");
            return sb.ToString();
        }

        static string Quote(string value) => JsonSerializer.Serialize(value);

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

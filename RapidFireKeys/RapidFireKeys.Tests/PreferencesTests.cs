using System.Text.Json;
using Shouldly;
using Xunit;

namespace RapidFireKeys.Tests;

/// <summary>
/// The settings file in %AppData%, and the files Export writes and Import reads. Whatever the
/// Settings window saves or exports has to come back meaning exactly what was on screen, and a
/// CONFIG.json from before 1.1.0 has to import as the same hotkeys.
/// </summary>
public class PreferencesTests
{
    private static Dictionary<string, List<KeyBinding>> SampleHotkeys() => new()
    {
        ["D2R.exe"] = new()
        {
            new KeyBinding { Key = "LButton", Modifiers = new() { "Shift", "Ctrl" } },
            new KeyBinding { Key = "Q" },
        },
        ["Empty.exe"] = new(),
        ["Odd \"name\".exe"] = new() { new KeyBinding { Key = "F1" } },
    };

    [Fact]
    public void ANewInstallStartsWithTheDefaults()
    {
        var prefs = new Preferences();

        prefs.Hotkeys.Keys.ShouldBe(new[] { "D2R.exe" });
        prefs.Theme.ShouldBe(ThemeMode.System);
        prefs.CheckForUpdatesAutomatically.ShouldBeTrue();
    }

    [Fact]
    public void WhatIsSavedReadsBackTheSame()
    {
        var original = new Preferences
        {
            Hotkeys = SampleHotkeys(),
            Theme = ThemeMode.Dark,
            CheckForUpdatesAutomatically = false,
            LastUpdateCheckUtc = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero),
        };

        var back = Preferences.Parse(original.Serialize());

        back.Hotkeys.Keys.ShouldBe(original.Hotkeys.Keys);
        back.Hotkeys["D2R.exe"][0].Modifiers.ShouldBe(new[] { "Shift", "Ctrl" });
        back.Hotkeys["D2R.exe"][1].Modifiers.ShouldBeNull();
        back.Hotkeys["Empty.exe"].ShouldBeEmpty();
        back.Hotkeys["Odd \"name\".exe"][0].Key.ShouldBe("F1");
        back.Theme.ShouldBe(ThemeMode.Dark);
        back.CheckForUpdatesAutomatically.ShouldBeFalse();
        back.LastUpdateCheckUtc.ShouldBe(original.LastUpdateCheckUtc);
    }

    /// <summary>
    /// A user who removed every app meant it. An empty list must not come back as the defaults.
    /// </summary>
    [Fact]
    public void AnEmptyHotkeyListStaysEmpty()
    {
        var back = Preferences.Parse(new Preferences { Hotkeys = new() }.Serialize());

        back.Hotkeys.ShouldBeEmpty();
    }

    [Fact]
    public void TheThemeIsSavedByName()
    {
        new Preferences { Theme = ThemeMode.Light }.Serialize().ShouldContain("\"theme\": \"Light\"");
    }

    [Fact]
    public void AnExportRoundTripsThroughImport()
    {
        var json = Preferences.Export(SampleHotkeys(), ThemeMode.Light, checkForUpdates: false);

        var (imported, hasPreferences) = Preferences.Import(json);

        hasPreferences.ShouldBeTrue();
        imported.Hotkeys.Keys.ShouldBe(SampleHotkeys().Keys);
        imported.Hotkeys["D2R.exe"][0].Modifiers.ShouldBe(new[] { "Shift", "Ctrl" });
        imported.Theme.ShouldBe(ThemeMode.Light);
        imported.CheckForUpdatesAutomatically.ShouldBeFalse();
    }

    /// <summary>When this machine last checked for updates is nobody else's business.</summary>
    [Fact]
    public void AnExportLeavesOutTheUpdateCheckClock()
    {
        Preferences.Export(SampleHotkeys(), ThemeMode.System, true).ShouldNotContain("lastUpdateCheck", Case.Insensitive);
    }

    [Fact]
    public void AnOldConfigJsonImportsAsHotkeysOnly()
    {
        const string legacy = """
            {
                "D2R.exe": [
                    { "key": "LButton", "modifiers": ["Shift"] },
                    { "key": "RButton" },
                ],
                "OtherApp.exe": [ { "key": "1" } ]
            }
            """;

        var (imported, hasPreferences) = Preferences.Import(legacy);

        hasPreferences.ShouldBeFalse();
        imported.Hotkeys["D2R.exe"].Count.ShouldBe(2);
        imported.Hotkeys["OtherApp.exe"][0].Key.ShouldBe("1");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"hello\"")]
    [InlineData("""{ "name": "something else", "version": 3 }""")]
    [InlineData("not json")]
    public void AFileThatIsNotSettingsIsRefused(string json)
    {
        Should.Throw<JsonException>(() => Preferences.Import(json));
    }

    [Fact]
    public void NullsInAHandWrittenFileAreDropped()
    {
        var prefs = Preferences.Parse("""{ "hotkeys": { "A.exe": null, "B.exe": [ null, { "key": "Q" } ] } }""");

        prefs.Hotkeys["A.exe"].ShouldBeEmpty();
        prefs.Hotkeys["B.exe"].Single().Key.ShouldBe("Q");
    }
}

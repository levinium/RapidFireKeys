using Shouldly;
using Xunit;

namespace RapidFireKeys.Tests;

/// <summary>
/// CONFIG.json, as read by the app and written by the Settings window. Existing hand-written
/// configs have to keep working, and a config the Settings window saves has to mean exactly what
/// was on screen.
/// </summary>
public class HotkeyConfigTests
{
    [Fact]
    public void TheDefaultsAreDiablo2WithShiftOnlyForLeftClick()
    {
        var defaults = HotkeyConfig.Defaults();

        defaults.Keys.ShouldBe(new[] { "D2R.exe" });
        var d2r = defaults["D2R.exe"];
        d2r.Count.ShouldBe(17);
        d2r[0].Key.ShouldBe("LButton");
        d2r[0].Modifiers.ShouldBe(new[] { "Shift" });
        d2r.Skip(1).ShouldAllBe(b => b.Modifiers == null);
    }

    /// <summary>
    /// The README's own example once had a trailing comma, so configs copied from it do too.
    /// Before 1.1.0 such a file stopped the app from rapid-firing anything at all.
    /// </summary>
    [Fact]
    public void AHandEditedConfigWithTrailingCommasAndCommentsIsRead()
    {
        const string json = """
            {
                // my games
                "D2R.exe": [
                    { "key": "LButton", "modifiers": ["Shift"] },
                    { "key": "RButton" },
                ],
                "OtherApp.exe": [ { "key": "1" } ],
            }
            """;

        var config = HotkeyConfig.Parse(json);

        config["D2R.exe"].Count.ShouldBe(2);
        config["OtherApp.exe"][0].Key.ShouldBe("1");
    }

    [Fact]
    public void WhatIsSavedReadsBackTheSame()
    {
        var original = new Dictionary<string, List<KeyBinding>>
        {
            ["D2R.exe"] = new()
            {
                new KeyBinding { Key = "LButton", Modifiers = new() { "Shift", "Ctrl" } },
                new KeyBinding { Key = "Q" },
            },
            ["Empty.exe"] = new(),
            ["Odd \"name\".exe"] = new() { new KeyBinding { Key = "F1" } },
        };

        var text = HotkeyConfig.Serialize(original);
        var roundTripped = HotkeyConfig.Parse(text);

        roundTripped.Keys.ShouldBe(original.Keys);
        roundTripped["D2R.exe"][0].Modifiers.ShouldBe(new[] { "Shift", "Ctrl" });
        roundTripped["D2R.exe"][1].Modifiers.ShouldBeNull();
        roundTripped["Empty.exe"].ShouldBeEmpty();
        roundTripped["Odd \"name\".exe"][0].Key.ShouldBe("F1");
    }

    [Fact]
    public void ProcessNamesMatchRegardlessOfCase()
    {
        var map = HotkeyConfig.Compile(HotkeyConfig.Defaults(), _ => { });

        map.ContainsKey("d2r.exe").ShouldBeTrue();
        map.ContainsKey("D2R.EXE").ShouldBeTrue();
    }

    [Fact]
    public void InvalidKeysAreSkippedAndReported()
    {
        var log = new List<string>();
        var config = HotkeyConfig.Parse("""{ "App.exe": [ { "key": "Q" }, { "key": "NotAKey" }, { "key": "W", "modifiers": ["Hyper"] } ] }""");

        var map = HotkeyConfig.Compile(config, log.Add);

        map["App.exe"].Select(b => b.key).ShouldBe(new[] { Keys.Q, Keys.W });
        map["App.exe"][1].modifiers.ShouldBeEmpty();
        log.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("Q", "Q")]
    [InlineData("q", "Q")]
    [InlineData(" lbutton ", "LButton")]
    [InlineData("1", "D1")]
    [InlineData("D1", "D1")]
    [InlineData("F12", "F12")]
    public void KeyNamesAreReadForgivingly(string name, string expected)
    {
        HotkeyConfig.TryParseKey(name, out var key).ShouldBeTrue();
        key.ToString().ShouldBe(expected);
    }

    /// <summary>Enum.TryParse alone would accept a number and turn "65" into A.</summary>
    [Theory]
    [InlineData("65")]
    [InlineData("200")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Hyper")]
    public void NonKeysAreRefused(string? name)
    {
        HotkeyConfig.TryParseKey(name, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("D2R", "D2R.exe")]
    [InlineData("D2R.exe", "D2R.exe")]
    [InlineData("  notepad.EXE ", "notepad.EXE")]
    [InlineData(@"C:\Games\Diablo II Resurrected\D2R.exe", "D2R.exe")]
    [InlineData("\"D2R.exe\"", "D2R.exe")]
    public void ProcessNamesAreNormalizedToTheExeFileName(string typed, string expected)
    {
        HotkeyConfig.NormalizeProcessName(typed).ShouldBe(expected);
    }
}

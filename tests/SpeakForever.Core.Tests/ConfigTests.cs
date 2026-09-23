using System.Text.Json;
using SpeakForever.Configuration;

namespace SpeakForever.Core.Tests;

/// <summary>The hand-editable settings file: saving, loading, and rejecting what would be lost or misread.</summary>
public sealed class ConfigTests
{
    public ConfigTests() => TestSetup.ResetSettingsFile();

    static Task<Config> Load() => Config.LoadOrCreateAsync(TestContext.Current.CancellationToken);

    static void Write(string json)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.Config, json);
    }

    [Fact]
    public async Task AFirstLoadWritesTheDefaults()
    {
        var cfg = await Load();
        Assert.Equal(new Config().SilenceMs, cfg.SilenceMs);
        Assert.True(File.Exists(AppPaths.Config));
        Assert.False(File.Exists(AppPaths.Config + ".tmp"));
    }

    [Fact]
    public async Task AnOldDefaultPromptMovesToTheNewOneButYourOwnIsKept()
    {
        const string Old = "World of Warcraft chat. Ironforge, Stormwind, Orgrimmar, Undercity, Darnassus, Thunder Bluff, " +
                           "Deadmines, Westfall, Elwynn Forest, Stranglethorn, Molten Core, Onyxia, Blackrock, Hyjal, Skyborne.";
        Write($$"""{ "Prompt": "{{Old}}" }""");
        Assert.Equal(Config.DefaultPrompt, (await Load()).Prompt);

        Write("""{ "Prompt": "Our guild is Wrathbringers." }""");
        Assert.Equal("Our guild is Wrathbringers.", (await Load()).Prompt);
    }

    [Fact]
    public async Task SettingsSurviveASaveAndReload()
    {
        await (new Config() with { SilenceMs = 2200, KeyboardShortcut = "Ctrl+Shift+Space", ProcessNames = ["WowB", "Wow"] })
            .SaveAsync(TestContext.Current.CancellationToken);
        var cfg = await Load();
        Assert.Equal(2200, cfg.SilenceMs);
        Assert.Equal("Ctrl+Shift+Space", cfg.KeyboardShortcut);
        Assert.Equal(["WowB", "Wow"], cfg.ProcessNames);
    }

    [Fact]
    public async Task AMisspeltSettingIsAnError()
    {
        Write("""{ "SilenceMS": 2000, "SilenceMz": 1 }""");
        var e = await Assert.ThrowsAsync<JsonException>(Load);
        Assert.Contains("SilenceMz", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommentsAreRefusedRatherThanLostOnTheNextSave()
    {
        Write("{ // my notes\n  \"SilenceMs\": 2000 }");
        await Assert.ThrowsAsync<JsonException>(Load);
    }

    [Fact]
    public async Task NullForARequiredSettingIsAnError()
    {
        Write("""{ "ModelPath": null }""");
        await Assert.ThrowsAsync<JsonException>(Load);
    }

    [Theory]
    [InlineData("""{ "SilenceMs": 50 }""", "SilenceMs")]
    [InlineData("""{ "ControllerSlot": 7 }""", "ControllerSlot")]
    [InlineData("""{ "ProcessNames": [] }""", "ProcessNames")]
    [InlineData("""{ "BeamSize": 0 }""", "BeamSize")]
    public async Task OutOfRangeValuesAreNamed(string json, string setting)
    {
        Write(json);
        var e = await Assert.ThrowsAsync<FormatException>(Load);
        Assert.StartsWith(setting, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangesMadeAtOnceAreBothKept()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var engine = TestSetup.NewEngine();
        await Task.WhenAll(
            Task.Run(() => engine.UpdateConfigAsync(c => c with { SilenceMs = 2500 }, ct), ct),
            Task.Run(() => engine.UpdateConfigAsync(c => c with { DelayMs = 300 }, ct), ct));
        Assert.Equal((2500, 300), (engine.Config.SilenceMs, engine.Config.DelayMs));
        var saved = await Load();
        Assert.Equal((2500, 300), (saved.SilenceMs, saved.DelayMs));
    }

    [Fact]
    public async Task AnOutOfRangeChangeLeavesTheSettingsAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var engine = TestSetup.NewEngine();
        await Assert.ThrowsAsync<FormatException>(() => engine.UpdateConfigAsync(c => c with { SilenceMs = -1 }, ct));
        Assert.Equal(new Config().SilenceMs, engine.Config.SilenceMs);
    }
}

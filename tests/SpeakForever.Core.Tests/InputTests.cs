using SpeakForever.Input;

namespace SpeakForever.Core.Tests;

/// <summary>Parsing chords and keyboard shortcuts, and recording a chord from presses.</summary>
public sealed class InputTests
{
    [Theory]
    [InlineData("Ctrl+Shift+Space", "Ctrl+Shift+Space")]
    [InlineData("f9", "F9")]
    [InlineData("ctrl+alt+num5", "Ctrl+Alt+Num5")]
    [InlineData("Shift+Ctrl+A", "Ctrl+Shift+A")]
    public void ShortcutsRoundTrip(string text, string expected) => Assert.Equal(expected, Shortcut.Parse(text).ToString());

    [Fact]
    public void EveryNamedKeyOnThisKeyboardLayoutRoundTrips()
    {
        int named = 0;
        for (uint key = 0x08; key <= 0xFE; key++)
        {
            if (Shortcut.NameOf(key) is null) continue;
            named++;
            var shortcut = new Shortcut(Shortcut.Ctrl | Shortcut.Shift, key);
            Assert.Equal(shortcut, Shortcut.Parse(shortcut.ToString()));
        }
        Assert.True(named > 90, $"only {named} keys have names"); // letters, digits, F keys, numpad, punctuation
    }

    [Theory]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+Shift")]
    public void BadShortcutsAreRejected(string text) => Assert.Throws<FormatException>(() => Shortcut.Parse(text));

    [Fact]
    public void AChordFiresOnlyWithExactlyItsModifiersHeld()
    {
        var chord = Chord.Parse("LB+RB+DOWN");
        uint held = Gamepad.LB | Gamepad.RB;
        Assert.True(chord.FiredBy(held, held | Gamepad.Down));
        Assert.False(chord.FiredBy(Gamepad.LB, Gamepad.LB | Gamepad.Down));
        Assert.False(chord.FiredBy(held | Gamepad.A, held | Gamepad.A | Gamepad.Down));
    }

    [Fact]
    public void TheRecorderTakesTheLastButtonAsTheKey()
    {
        var recorder = new ChordRecorder();
        Assert.Null(recorder.Step(0, Gamepad.LT));
        Assert.Null(recorder.Step(Gamepad.LT, Gamepad.LT | Gamepad.RT));
        Assert.Null(recorder.Step(Gamepad.LT | Gamepad.RT, Gamepad.LT | Gamepad.RT | Gamepad.Up));
        Assert.Null(recorder.Step(Gamepad.LT | Gamepad.RT | Gamepad.Up, Gamepad.LT));
        Assert.Equal("LT+RT+UP", recorder.Step(Gamepad.LT, 0)?.Text);
    }

    [Fact]
    public void UnknownButtonsAreRejected() => Assert.Throws<FormatException>(() => Chord.Parse("LB+Z"));

    [Fact]
    public async Task AnyKeyCanBeTheShortcutWithOrWithoutModifiers()
    {
        var ct = TestContext.Current.CancellationToken;
        TestSetup.ResetSettingsFile();
        await using var engine = TestSetup.NewEngine();
        Assert.Null(await engine.SetKeyboardShortcutAsync(Shortcut.Parse("V"), ct));
        Assert.Equal("V", engine.Config.KeyboardShortcut);
        Assert.Null(await engine.SetKeyboardShortcutAsync(Shortcut.Parse("Ctrl+F9"), ct));
        Assert.Equal("Ctrl+F9", engine.Config.KeyboardShortcut);
    }
}

using VoiceForever.Input;

namespace VoiceForever.Core.Tests;

/// <summary>Parsing chords and keyboard shortcuts, and recording a chord from presses.</summary>
public sealed class InputTests
{
    [Theory]
    [InlineData("Ctrl+Shift+Space", "Ctrl+Shift+Space")]
    [InlineData("f9", "F9")]
    [InlineData("ctrl+alt+num5", "Ctrl+Alt+Num5")]
    [InlineData("Shift+Ctrl+A", "Ctrl+Shift+A")]
    public void ShortcutsRoundTrip(string text, string expected) => Assert.Equal(expected, Shortcut.Parse(text).ToString());

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
    public async Task ABareLetterShortcutIsRefusedButAnFKeyIsAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        TestSetup.ResetSettingsFile();
        await using var engine = TestSetup.NewEngine();
        Assert.Contains("every program", await engine.SetKeyboardShortcutAsync(Shortcut.Parse("V"), ct), StringComparison.Ordinal);
        Assert.Null(await engine.SetKeyboardShortcutAsync(Shortcut.Parse("F9"), ct));
        Assert.Equal("F9", engine.Config.KeyboardShortcut);
    }
}

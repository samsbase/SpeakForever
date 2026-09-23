using SpeakForever.Presentation;

namespace SpeakForever.Core.Tests;

/// <summary>The Home tab's status: the most pressing thing wins.</summary>
public sealed class HomeStatusTests
{
    static HomeStatus Of(string? error = null, bool running = true, DictationPhase phase = DictationPhase.Idle, bool hasModel = true,
                         bool loading = false, bool gameMissing = false, bool controller = true, bool chatOpen = false, string? key = null) =>
        HomeStatus.Of(error, running, phase, hasModel, loading, gameMissing, controller, chatOpen, key);

    [Fact]
    public void AllSetIsReadyAndSaysWhichButtonsToPress()
    {
        var status = Of();
        Assert.Equal(("Ready", StatusTone.Ready), (status.Headline, status.Tone));
        Assert.Contains("{0}", status.Detail, StringComparison.Ordinal);
        Assert.Contains("{1}", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ProblemsComeBeforeReadiness()
    {
        Assert.Equal(StatusTone.Problem, Of(error: "No microphone").Tone);
        Assert.Equal("Get a voice model", Of(hasModel: false).Headline);
        Assert.Equal("Where's WoW: Forever?", Of(gameMissing: true).Headline);
        Assert.Equal("Paused", Of(running: false, hasModel: false).Headline);
    }

    [Fact]
    public void ADictationShowsWhileItRuns()
    {
        Assert.Equal(StatusTone.Busy, Of(phase: DictationPhase.Listening).Tone);
        Assert.Equal("Typing it into chat", Of(phase: DictationPhase.Transcribing, chatOpen: true).Headline);
    }

    [Fact]
    public void WithoutAControllerTheKeyboardShortcutIsEnough()
    {
        Assert.Equal("Connect a controller", Of(controller: false).Headline);
        var keyboard = Of(controller: false, key: "F8");
        Assert.Equal(("Ready", StatusTone.Ready), (keyboard.Headline, keyboard.Tone));
        Assert.DoesNotContain("{", keyboard.Detail, StringComparison.Ordinal);
        Assert.Contains("F8", Of(controller: false, key: "F8", phase: DictationPhase.Listening).Detail, StringComparison.Ordinal);
    }
}

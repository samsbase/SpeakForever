using VoiceForever.Input;

namespace VoiceForever.Core.Tests;

/// <summary>Following WoW's gamepad chat panel from simulated presses (probe mode: nothing is typed).</summary>
public sealed class ChatPanelTests : IAsyncLifetime
{
    readonly Engine engine = TestSetup.NewEngine();

    public ValueTask InitializeAsync()
    {
        TestSetup.ResetSettingsFile();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => engine.DisposeAsync();

    [Fact]
    public void DictateAloneDoesNotCountAsChatOpen()
    {
        engine.Press("RS");
        Assert.False(engine.ChatOpen);
    }

    [Fact]
    public void TheChannelMenuTakesTwoPressesOfAToGetBack()
    {
        engine.Press("LB+RB+DOWN");
        Assert.True(engine.ChatOpen);
        engine.Press("X");
        engine.Press("A");
        Assert.False(engine.ChatOpen); // in the channel submenu
        engine.Press("A");
        Assert.True(engine.ChatOpen); // picked a channel, back in the text box
        engine.Press("A");
        Assert.False(engine.ChatOpen); // sent
    }

    [Fact]
    public void BackLeavesAMenuOneLevelAtATime()
    {
        engine.Press("LB+RB+DOWN");
        engine.Press("Y");
        Assert.False(engine.ChatOpen);
        engine.Press("B");
        Assert.True(engine.ChatOpen);
        engine.Press("B");
        Assert.False(engine.ChatOpen);
    }

    [Fact]
    public async Task ABindingThatClashesWithThePanelIsRefused()
    {
        var error = await engine.SetBindingAsync(BindingKind.Dictate, Chord.Parse("A"), TestContext.Current.CancellationToken);
        Assert.Contains("already used", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReboundOpenChatComboWorksAndIsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Null(await engine.SetBindingAsync(BindingKind.OpenChat, Chord.Parse("LT+RT+UP"), ct));
        engine.Press("LT+RT+UP");
        Assert.True(engine.ChatOpen);
        Assert.Equal("LT+RT+UP", (await Configuration.Config.LoadOrCreateAsync(ct)).OpenChatChord);
    }
}

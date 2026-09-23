using SpeakForever.Input;

namespace SpeakForever.Core.Tests;

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
    public void StartingOverIsOnlyForTheTextBox()
    {
        var b = ControllerBindings.From(new Configuration.Config());
        var panel = new ChatPanel();
        var down = Chord.Parse("DOWN");
        Assert.Equal(ChatAction.None, panel.OnButtons(0, down.Key, b)); // chat closed: D-pad down is the game's
        panel.Open();
        Assert.Equal(ChatAction.Redo, panel.OnButtons(0, down.Key, b));
        Assert.True(panel.InTextBox);
        Assert.Equal(ChatAction.MenuOpened, panel.OnButtons(0, Gamepad.X, b));
        Assert.Equal(ChatAction.None, panel.OnButtons(0, down.Key, b)); // moves through the channel menu
        Assert.Equal(ChatAction.None, panel.OnButtons(Gamepad.LB | Gamepad.RB, Gamepad.LB | Gamepad.RB | Gamepad.Down, b)); // reopens chat
        Assert.True(panel.InTextBox);
    }

    [Fact]
    public async Task StartingOverCanBeReboundButNotOntoAnotherButton()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Contains("already used", await engine.SetBindingAsync(BindingKind.Redo, Chord.Parse("RS"), ct), StringComparison.Ordinal);
        Assert.Null(await engine.SetBindingAsync(BindingKind.Redo, Chord.Parse("LT+DOWN"), ct));
        Assert.Equal("LT+DOWN", (await Configuration.Config.LoadOrCreateAsync(ct)).RedoChord);
        Assert.Contains("starting over", await engine.SetBindingAsync(BindingKind.Dictate, Chord.Parse("LT+DOWN"), ct), StringComparison.Ordinal);
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

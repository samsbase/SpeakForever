namespace SpeakForever.Core.Tests;

/// <summary>Opening chat from WoW's radial menu, per GamepadRadial.lua.</summary>
public sealed class RadialMenuTests : IAsyncLifetime
{
    readonly Engine engine = TestSetup.NewEngine();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => engine.DisposeAsync();

    [Fact]
    public void PickingChatOnTheMainMenuPageOpensChat()
    {
        engine.Press("START");
        Assert.False(engine.ChatOpen);
        engine.PickDownLeft();
        Assert.True(engine.ChatOpen);
    }

    [Fact]
    public void TheSameSpotOnAnotherPageIsNotChat()
    {
        engine.Press("START");
        engine.Press("LB"); // page 1: Buffs sits there
        engine.PickDownLeft();
        Assert.False(engine.ChatOpen);
    }

    [Fact]
    public void PagesWrapBothWays()
    {
        engine.Press("START");
        engine.Press("LB");
        engine.Press("LB");
        engine.Press("RB");
        engine.Press("RB");
        engine.PickDownLeft();
        Assert.True(engine.ChatOpen);
    }

    [Fact]
    public void HoldingTheStickWhileCyclingRoundThenReleasingOpensChat()
    {
        engine.Press("START");
        for (int i = 0; i < 3; i++)
        {
            engine.Stick(-0.7f, -0.7f);
            engine.Press("RB");
        }
        engine.Stick(0f, 0f);
        Assert.True(engine.ChatOpen);
    }

    [Fact]
    public void ASpringBackOvershootOfOnePollIsIgnored()
    {
        engine.Press("START");
        engine.Stick(-0.7f, -0.7f);
        engine.OnRightStick(0.6f, 0.6f); // one poll on Professions, opposite Chat
        engine.OnRightStick(0f, 0f);
        Assert.True(engine.ChatOpen);
    }

    [Fact]
    public void ReleasingOnAnEmptySlotLeavesTheMenuOpen()
    {
        engine.Press("START");
        engine.Press("RB"); // page 3: mostly empty
        engine.PickDownLeft();
        Assert.False(engine.ChatOpen);
        engine.Press("LB");
        engine.PickDownLeft();
        Assert.True(engine.ChatOpen);
    }

    [Fact]
    public void ClickingTheStickCancelsUntilItRecentres()
    {
        engine.Press("START");
        engine.Stick(-0.7f, -0.7f);
        engine.Press("RS");
        engine.Stick(0f, 0f);
        Assert.False(engine.ChatOpen);
        engine.PickDownLeft();
        Assert.True(engine.ChatOpen);
    }

    [Fact]
    public void BClosesTheMenuAndLaterStickMovesAreIgnored()
    {
        engine.Press("START");
        engine.Press("B");
        engine.PickDownLeft();
        Assert.False(engine.ChatOpen);
    }

    [Fact]
    public void PickingSomethingElseDoesNotOpenChat()
    {
        engine.Press("START");
        engine.Stick(0.7f, 0f); // Bags
        engine.Stick(0f, 0f);
        Assert.False(engine.ChatOpen);
        engine.Press("LB+RB+DOWN");
        Assert.True(engine.ChatOpen);
    }
}

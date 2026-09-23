using SDL;
using SpeakForever.Input;
using SpeakForever.Presentation;

namespace SpeakForever.Core.Tests;

/// <summary>Which icon and name each controller button gets on each kind of controller.</summary>
public sealed class ButtonIconTests
{
    static readonly uint[] AllButtons = [.. Gamepad.Each(uint.MaxValue)];

    static string IconFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SpeakForever.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "app", "Gui", "Assets", ButtonIcons.Folder);
    }

    [Fact]
    public void EveryButtonHasAnIconThatShipsWithTheApp()
    {
        var folder = IconFolder();
        foreach (var style in Enum.GetValues<ButtonStyle>())
            foreach (var button in AllButtons)
                Assert.True(File.Exists(Path.Combine(folder, ButtonIcons.File(style, button))), $"{style} {Gamepad.Describe(button)}");
        Assert.All(ButtonIcons.AllFiles, f => Assert.True(File.Exists(Path.Combine(folder, f)), f));
        Assert.Equal(16, AllButtons.Length);
    }

    [Theory]
    [InlineData(ButtonStyle.Xbox, Gamepad.A, "xbox_button_a.svg")]
    [InlineData(ButtonStyle.PlayStation5, Gamepad.A, "playstation_button_cross.svg")]
    [InlineData(ButtonStyle.Nintendo, Gamepad.A, "switch_button_b.svg")] // the bottom button is B on Nintendo's
    [InlineData(ButtonStyle.Nintendo, Gamepad.B, "switch_button_a.svg")]
    [InlineData(ButtonStyle.PlayStation4, Gamepad.Back, "playstation4_button_share.svg")]
    [InlineData(ButtonStyle.PlayStation5, Gamepad.Back, "playstation5_button_create.svg")]
    [InlineData(ButtonStyle.PlayStation3, Gamepad.Start, "playstation3_button_start.svg")]
    public void IconsFollowTheController(ButtonStyle style, uint button, string file) => Assert.Equal(file, ButtonIcons.File(style, button));

    [Theory]
    [InlineData(ButtonStyle.Xbox, "LB+RB+DOWN", "LB plus RB plus D-pad down")]
    [InlineData(ButtonStyle.PlayStation5, "LB+RB+DOWN", "L1 plus R1 plus D-pad down")]
    [InlineData(ButtonStyle.Nintendo, "LT+A", "ZL plus B")]
    [InlineData(ButtonStyle.Xbox, "RS", "right stick press")]
    public void ScreenReadersHearTheNamesPrintedOnTheController(ButtonStyle style, string chord, string spoken) =>
        Assert.Equal(spoken, ButtonIcons.Spoken(style, Chord.Parse(chord)));

    [Fact]
    public void ModifiersComeBeforeTheKey() =>
        Assert.Equal([Gamepad.LB, Gamepad.RB, Gamepad.Down], ButtonIcons.Buttons(Chord.Parse("LB+RB+DOWN")));

    [Theory]
    [InlineData(SDL_GamepadType.SDL_GAMEPAD_TYPE_XBOXONE, ButtonStyle.Xbox)]
    [InlineData(SDL_GamepadType.SDL_GAMEPAD_TYPE_STANDARD, ButtonStyle.Xbox)]
    [InlineData(SDL_GamepadType.SDL_GAMEPAD_TYPE_UNKNOWN, ButtonStyle.Xbox)]
    [InlineData(SDL_GamepadType.SDL_GAMEPAD_TYPE_PS4, ButtonStyle.PlayStation4)]
    [InlineData(SDL_GamepadType.SDL_GAMEPAD_TYPE_PS5, ButtonStyle.PlayStation5)]
    [InlineData(SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_PRO, ButtonStyle.Nintendo)]
    [InlineData(SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_PAIR, ButtonStyle.Nintendo)]
    internal void SdlsControllerTypesPickTheIcons(SDL_GamepadType type, ButtonStyle style) => Assert.Equal(style, GamepadReader.StyleOf(type));
}

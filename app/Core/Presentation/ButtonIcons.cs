using SpeakForever.Input;

namespace SpeakForever.Presentation;

/// <summary>
/// Which of Kenney's Input Prompts icons (CC0, kenney.nl) shows each button, for each family of
/// controller. Buttons are positional, as SDL and WoW treat them: the bottom face button is "A"
/// here, and it's Cross on a PlayStation controller and B on a Nintendo one.
/// </summary>
public static class ButtonIcons
{
    /// <summary>The folder under the app's Assets that holds the icons.</summary>
    public const string Folder = "Buttons";

    static readonly Dictionary<uint, string> Xbox = new()
    {
        [Gamepad.A] = "xbox_button_a", [Gamepad.B] = "xbox_button_b", [Gamepad.X] = "xbox_button_x", [Gamepad.Y] = "xbox_button_y",
        [Gamepad.LB] = "xbox_lb", [Gamepad.RB] = "xbox_rb", [Gamepad.LT] = "xbox_lt", [Gamepad.RT] = "xbox_rt",
        [Gamepad.Back] = "xbox_button_view", [Gamepad.Start] = "xbox_button_menu",
        [Gamepad.LS] = "xbox_stick_l_press", [Gamepad.RS] = "xbox_stick_r_press",
        [Gamepad.Up] = "xbox_dpad_up", [Gamepad.Down] = "xbox_dpad_down", [Gamepad.Left] = "xbox_dpad_left", [Gamepad.Right] = "xbox_dpad_right",
    };

    static readonly Dictionary<uint, string> PlayStation = new()
    {
        [Gamepad.A] = "playstation_button_cross", [Gamepad.B] = "playstation_button_circle",
        [Gamepad.X] = "playstation_button_square", [Gamepad.Y] = "playstation_button_triangle",
        [Gamepad.LB] = "playstation_trigger_l1", [Gamepad.RB] = "playstation_trigger_r1",
        [Gamepad.LT] = "playstation_trigger_l2", [Gamepad.RT] = "playstation_trigger_r2",
        [Gamepad.Back] = "playstation5_button_create", [Gamepad.Start] = "playstation5_button_options",
        [Gamepad.LS] = "playstation_stick_l_press", [Gamepad.RS] = "playstation_stick_r_press",
        [Gamepad.Up] = "playstation_dpad_up", [Gamepad.Down] = "playstation_dpad_down",
        [Gamepad.Left] = "playstation_dpad_left", [Gamepad.Right] = "playstation_dpad_right",
    };

    // Nintendo's letters are swapped from Xbox's: the bottom button is B, the right one A.
    static readonly Dictionary<uint, string> Nintendo = new()
    {
        [Gamepad.A] = "switch_button_b", [Gamepad.B] = "switch_button_a", [Gamepad.X] = "switch_button_y", [Gamepad.Y] = "switch_button_x",
        [Gamepad.LB] = "switch_button_l", [Gamepad.RB] = "switch_button_r", [Gamepad.LT] = "switch_button_zl", [Gamepad.RT] = "switch_button_zr",
        [Gamepad.Back] = "switch_button_minus", [Gamepad.Start] = "switch_button_plus",
        [Gamepad.LS] = "switch_stick_l_press", [Gamepad.RS] = "switch_stick_r_press",
        [Gamepad.Up] = "switch_dpad_up", [Gamepad.Down] = "switch_dpad_down", [Gamepad.Left] = "switch_dpad_left", [Gamepad.Right] = "switch_dpad_right",
    };

    /// <summary>Every icon file the app ships.</summary>
    public static IEnumerable<string> AllFiles =>
        new[] { Xbox, PlayStation, Nintendo }.SelectMany(d => d.Values)
            .Concat(["playstation3_button_select", "playstation3_button_start", "playstation4_button_share", "playstation4_button_options"])
            .Select(n => n + ".svg").Distinct();

    /// <summary>The icon file for one button, such as "xbox_lb.svg".</summary>
    public static string File(ButtonStyle style, uint button)
    {
        var name = (style, button) switch
        {
            (ButtonStyle.PlayStation3, Gamepad.Back) => "playstation3_button_select",
            (ButtonStyle.PlayStation3, Gamepad.Start) => "playstation3_button_start",
            (ButtonStyle.PlayStation4, Gamepad.Back) => "playstation4_button_share",
            (ButtonStyle.PlayStation4, Gamepad.Start) => "playstation4_button_options",
            (ButtonStyle.PlayStation3 or ButtonStyle.PlayStation4 or ButtonStyle.PlayStation5, _) => PlayStation[button],
            (ButtonStyle.Nintendo, _) => Nintendo[button],
            _ => Xbox[button],
        };
        return name + ".svg";
    }

    /// <summary>The buttons of a chord in order, modifiers first, for drawing one icon each.</summary>
    public static IReadOnlyList<uint> Buttons(Chord chord) =>
        [.. Gamepad.Each(chord.Modifiers), .. Gamepad.Each(chord.Key)];

    /// <summary>What a screen reader says for a chord on this controller: "L1 plus R1 plus D-pad down".</summary>
    public static string Spoken(ButtonStyle style, Chord chord) =>
        string.Join(" plus ", Buttons(chord).Select(b => Label(style, b)));

    /// <summary>The button's name as printed on this controller.</summary>
    public static string Label(ButtonStyle style, uint button) => (style, button) switch
    {
        (_, Gamepad.Up) => "D-pad up",
        (_, Gamepad.Down) => "D-pad down",
        (_, Gamepad.Left) => "D-pad left",
        (_, Gamepad.Right) => "D-pad right",
        (ButtonStyle.PlayStation3 or ButtonStyle.PlayStation4 or ButtonStyle.PlayStation5, _) => button switch
        {
            Gamepad.A => "Cross", Gamepad.B => "Circle", Gamepad.X => "Square", Gamepad.Y => "Triangle",
            Gamepad.LB => "L1", Gamepad.RB => "R1", Gamepad.LT => "L2", Gamepad.RT => "R2", Gamepad.LS => "L3", Gamepad.RS => "R3",
            Gamepad.Back => style == ButtonStyle.PlayStation3 ? "Select" : style == ButtonStyle.PlayStation4 ? "Share" : "Create",
            _ => style == ButtonStyle.PlayStation3 ? "Start" : "Options",
        },
        (ButtonStyle.Nintendo, _) => button switch
        {
            Gamepad.A => "B", Gamepad.B => "A", Gamepad.X => "Y", Gamepad.Y => "X",
            Gamepad.LB => "L", Gamepad.RB => "R", Gamepad.LT => "ZL", Gamepad.RT => "ZR",
            Gamepad.LS => "left stick press", Gamepad.RS => "right stick press", Gamepad.Back => "Minus", _ => "Plus",
        },
        _ => button switch
        {
            Gamepad.LS => "left stick press", Gamepad.RS => "right stick press", Gamepad.Back => "View", Gamepad.Start => "Menu",
            _ => Gamepad.Describe(button),
        },
    };
}

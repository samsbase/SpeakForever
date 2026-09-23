namespace SpeakForever.Input;

/// <summary>
/// Controller buttons as bit flags, and their names in chords ("LB+RB+DOWN"). The names are
/// positional, Xbox-style: "A" is the bottom face button on every controller (Cross on PlayStation,
/// B on Nintendo), which is how WoW and SDL treat them too. <see cref="GamepadReader"/> reads them.
/// </summary>
public static class Gamepad
{
    // XInput's flag values, kept from when the app read controllers through XInput. The triggers
    // are analog, so they get bits above the real ones.
    public const uint Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008;
    public const uint Start = 0x0010, Back = 0x0020, LS = 0x0040, RS = 0x0080;
    public const uint LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
    public const uint LT = 0x10000, RT = 0x20000;

    static readonly (string Name, uint Bit)[] Names =
    [
        ("LT", LT), ("RT", RT), ("LB", LB), ("RB", RB),
        ("A", A), ("B", B), ("X", X), ("Y", Y),
        ("UP", Up), ("DOWN", Down), ("LEFT", Left), ("RIGHT", Right),
        ("START", Start), ("BACK", Back), ("LS", LS), ("RS", RS),
    ];

    /// <exception cref="FormatException">Not a button name.</exception>
    public static uint Bit(string name)
    {
        foreach (var (n, b) in Names)
            if (n.Equals(name, StringComparison.OrdinalIgnoreCase)) return b;
        throw new FormatException($"Unknown button '{name}'. Valid: {string.Join(", ", Names.Select(n => n.Name))}.");
    }

    public static string Describe(uint mask) =>
        mask == 0 ? "(none)" : string.Join("+", Names.Where(n => (mask & n.Bit) != 0).Select(n => n.Name));

    /// <summary>Each button in a mask, in chord order (triggers and bumpers first).</summary>
    public static IEnumerable<uint> Each(uint mask) => Names.Where(n => (mask & n.Bit) != 0).Select(n => n.Bit);
}

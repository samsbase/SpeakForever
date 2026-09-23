namespace SpeakForever.Input;

/// <summary>
/// A WoW-style binding: modifiers held first, then a key. "LT+RT+A" fires when A goes down while
/// exactly LT and RT are held — the same rule WoW applies to SHIFT-CTRL-PAD1 style bindings, so
/// the app fires on precisely the presses the game treats as that keybind.
/// </summary>
public readonly record struct Chord(string Text, uint Modifiers, uint Key)
{
    /// <exception cref="FormatException">Empty, or a name that isn't a button.</exception>
    public static Chord Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException("Empty chord.");
        uint mods = 0;
        foreach (var p in parts[..^1]) mods |= Gamepad.Bit(p);
        return new Chord(text, mods, Gamepad.Bit(parts[^1]));
    }

    /// <summary>Builds a chord from button masks, e.g. LB|RB and DOWN → "LB+RB+DOWN".</summary>
    public static Chord Of(uint modifiers, uint key) =>
        new(modifiers == 0 ? Gamepad.Describe(key) : Gamepad.Describe(modifiers) + "+" + Gamepad.Describe(key), modifiers, key);

    public bool FiredBy(uint prev, uint cur) =>
        (cur & Key) != 0 && (prev & Key) == 0 && (cur & ~Key) == Modifiers;

    public bool SameButtons(Chord other) => Modifiers == other.Modifiers && Key == other.Key;
}

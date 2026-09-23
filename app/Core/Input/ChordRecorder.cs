namespace SpeakForever.Input;

/// <summary>
/// Records one chord from live presses, the way WoW reads a binding: the last button to go down
/// is the key and anything already held is a modifier. It finishes once everything is released.
/// </summary>
public sealed class ChordRecorder
{
    uint modifiers, key;

    /// <summary>The recorded chord once all buttons are up, otherwise null.</summary>
    public Chord? Step(uint prev, uint cur)
    {
        uint pressed = cur & ~prev;
        if (pressed != 0)
        {
            // Buttons landing in the same poll: the shoulders and triggers are the likelier modifiers.
            uint rest = pressed & ~(Gamepad.LT | Gamepad.RT | Gamepad.LB | Gamepad.RB);
            uint pool = rest != 0 ? rest : pressed;
            key = pool & (uint)-(int)pool; // lowest set bit
            modifiers = cur & ~key;
        }
        return cur == 0 && key != 0 ? Chord.Of(modifiers, key) : null;
    }
}

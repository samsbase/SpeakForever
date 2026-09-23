using VoiceForever.Configuration;

namespace VoiceForever.Input;

/// <summary>The controller chords, parsed. Swapped as a whole on a rebind, so the controller thread never sees half a change.</summary>
sealed record ControllerBindings(Chord OpenChat, Chord Dictate, Chord Send, Chord Back, IReadOnlyList<Chord> Menus, Chord Radial)
{
    /// <exception cref="FormatException">A chord in the settings doesn't parse.</exception>
    public static ControllerBindings From(Config c) => new(
        Chord.Parse(c.OpenChatChord), Chord.Parse(c.DictateChord), Chord.Parse(c.SendChord), Chord.Parse(c.BackChord),
        [.. c.MenuChords.Select(Chord.Parse)], Chord.Parse(c.RadialMenuChord));
}

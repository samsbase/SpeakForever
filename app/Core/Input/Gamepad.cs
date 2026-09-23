using System.Runtime.InteropServices;

namespace SpeakForever.Input;

/// <summary>
/// XInput via xinput1_4.dll (ships with Windows). Covers Xbox-protocol pads; PlayStation pads
/// need Steam Input or DS4Windows to present as XInput. XInput has no exclusive mode, which is
/// what lets us read the pad while WoW is also reading it.
/// </summary>
public static partial class Gamepad
{
    // XInput's button flags. The triggers are analog, so they get virtual bits above the real ones.
    public const uint Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008;
    public const uint Start = 0x0010, Back = 0x0020, LS = 0x0040, RS = 0x0080;
    public const uint LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
    public const uint LT = 0x10000, RT = 0x20000;

    const byte TriggerThreshold = 30; // XINPUT_GAMEPAD_TRIGGER_THRESHOLD
    const int SlotCount = 4;

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

    /// <summary>Buttons and right stick for a slot, or null if nothing is connected there.</summary>
    public static PadState? Read(int slot)
    {
        if (XInputGetState((uint)slot, out var s) != 0) return null;
        uint mask = s.Pad.Buttons;
        if (s.Pad.LeftTrigger > TriggerThreshold) mask |= LT;
        if (s.Pad.RightTrigger > TriggerThreshold) mask |= RT;
        return new PadState(mask, Math.Max(-1f, s.Pad.RX / 32767f), Math.Max(-1f, s.Pad.RY / 32767f));
    }

    /// <summary>The preferred slot if a pad is there, or with -1 the first connected one; -1 if none.</summary>
    public static int FindSlot(int preferred)
    {
        if (preferred >= 0) return Read(preferred) is null ? -1 : preferred;
        for (int i = 0; i < SlotCount; i++)
            if (Read(i) is not null) return i;
        return -1;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XInputState
    {
        public uint Packet;
        public XInputGamepad Pad;
    }

    [LibraryImport("xinput1_4.dll")]
    private static partial uint XInputGetState(uint userIndex, out XInputState state);
}

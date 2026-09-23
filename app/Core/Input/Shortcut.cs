using System.Runtime.InteropServices;

namespace SpeakForever.Input;

/// <summary>A keyboard shortcut such as "Ctrl+Shift+Space": modifier flags plus one virtual key.</summary>
public readonly partial record struct Shortcut(uint Modifiers, uint Key)
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8; // RegisterHotKey's MOD_ values

    const uint FirstKey = 0x08, LastKey = 0xFE;

    static readonly Dictionary<uint, string> KeyNames = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "CapsLock",
        [0x1B] = "Esc", [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2D] = "Insert", [0x2E] = "Delete",
        [0x6A] = "Num*", [0x6B] = "NumPlus", [0x6D] = "Num-", [0x6E] = "Num.", [0x6F] = "Num/",
    };

    /// <summary>
    /// Punctuation keys, named by the keyboard layout in use: the key a US keyboard calls "'" is "#"
    /// on a UK one, and UK keyboards have two more (` beside 1, and \ beside left Shift).
    /// </summary>
    static readonly Dictionary<uint, string> PunctuationNames = BuildPunctuationNames();

    /// <summary>Every nameable key by name, for parsing; built once.</summary>
    static readonly Dictionary<string, uint> KeysByName = BuildKeysByName();

    static Dictionary<uint, string> BuildPunctuationNames()
    {
        const uint ToChar = 2; // MAPVK_VK_TO_CHAR
        var names = new Dictionary<uint, string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Plus" };
        foreach (uint key in (ReadOnlySpan<uint>)[0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0xE1, 0xE2])
        {
            // The low word is the key's unshifted character; the top bit marks a dead key (an accent).
            char ch = (char)(MapVirtualKeyW(key, ToChar) & 0xFFFF);
            if (ch <= ' ') continue; // not on this layout
            var name = ch == '+' ? "Plus" : ch.ToString(); // "+" joins the parts of a shortcut
            // Two keys with the same character would be ambiguous: the second is named by its code.
            names[key] = taken.Add(name) ? name : $"Key{key}";
        }
        return names;
    }

    static Dictionary<string, uint> BuildKeysByName()
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (uint k = FirstKey; k <= LastKey; k++)
            if (NameOf(k) is { } name) map.TryAdd(name, k);
        return map;
    }

    /// <summary>The key's display name, or null for keys a shortcut can't use.</summary>
    public static string? NameOf(uint key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x60 and <= 0x69 => $"Num{key - 0x60}",
        >= 0x70 and <= 0x87 => $"F{key - 0x6F}",
        _ => KeyNames.GetValueOrDefault(key) ?? PunctuationNames.GetValueOrDefault(key),
    };

    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & Ctrl) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Win) != 0) parts.Add("Win");
        parts.Add(NameOf(Key) ?? $"Key{Key}");
        return string.Join("+", parts);
    }

    /// <exception cref="FormatException">An unknown key name, or no key at all.</exception>
    public static Shortcut Parse(string text)
    {
        uint modifiers = 0, key = 0;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": modifiers |= Ctrl; break;
                case "ALT": modifiers |= Alt; break;
                case "SHIFT": modifiers |= Shift; break;
                case "WIN": modifiers |= Win; break;
                default:
                    if (!KeysByName.TryGetValue(part, out key))
                        throw new FormatException($"Unknown key '{part}' in shortcut '{text}'.");
                    break;
            }
        }
        if (key == 0) throw new FormatException($"Shortcut '{text}' has no key.");
        return new Shortcut(modifiers, key);
    }

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint code, uint mapType);
}

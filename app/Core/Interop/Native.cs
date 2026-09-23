using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpeakForever.Interop;

/// <summary>Typing into other windows, and finding out which one is in front.</summary>
public static partial class Native
{
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
    const int MaxTitleLength = 256;

    /// <summary>
    /// Types text into the focused window as Unicode characters, independent of keyboard layout.
    /// Returns null on success or an error description. Note that UIPI blocking (target window
    /// elevated, us not) is silent — SendInput reports success anyway.
    /// </summary>
    public static string? TypeText(string text)
    {
        var seq = new Input[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            seq[i * 2] = Char(text[i], up: false);
            seq[i * 2 + 1] = Char(text[i], up: true);
        }
        uint sent = SendInput((uint)seq.Length, seq, Marshal.SizeOf<Input>());
        return sent == seq.Length ? null : $"SendInput sent {sent}/{seq.Length} events (Win32 error {Marshal.GetLastPInvokeError()})";
    }

    static Input Char(char c, bool up) => new()
    {
        Type = INPUT_KEYBOARD,
        Union = new InputUnion { Keyboard = new KeybdInput { Scan = c, Flags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } },
    };

    /// <summary>The foreground window's process name (without .exe) and title.</summary>
    public static unsafe (string Process, string Title) Foreground()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return ("", "");
        GetWindowThreadProcessId(h, out var pid);
        string name;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch (ArgumentException)
        {
            name = "?"; // exited in between
        }
        char* title = stackalloc char[MaxTitleLength];
        int length = GetWindowText(h, title, MaxTitleLength);
        return (name, new string(title, 0, Math.Max(0, length)));
    }

    // The union must be sized for MOUSEINPUT, the largest member, or SendInput rejects cbSize.
    [StructLayout(LayoutKind.Sequential)]
    struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeybdInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KeybdInput
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static unsafe partial int GetWindowText(IntPtr hWnd, char* text, int maxCount);
}

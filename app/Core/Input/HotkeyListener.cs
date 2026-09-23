using System.Runtime.InteropServices;

namespace SpeakForever.Input;

/// <summary>
/// A system-wide keyboard shortcut. RegisterHotKey delivers WM_HOTKEY to the registering thread,
/// so it gets a thread and message loop of its own; the key press is consumed, not passed on.
/// </summary>
public sealed partial class HotkeyListener : IDisposable
{
    const int HotkeyId = 1;
    const uint MOD_NOREPEAT = 0x4000, WM_HOTKEY = 0x0312, WM_QUIT = 0x0012;

    Thread? thread;
    uint threadId;

    /// <summary>Raised on the listener's own thread.</summary>
    public event Action? Pressed;

    /// <summary>
    /// Starts listening; returns why it couldn't (usually another program owns the shortcut), or
    /// null. Blocks only until the listener thread has tried to register, a millisecond or so.
    /// </summary>
    public string? Register(Shortcut shortcut)
    {
        Unregister();
        string? error = null;
        using var ready = new ManualResetEventSlim();
        var listener = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            bool registered = RegisterHotKey(IntPtr.Zero, HotkeyId, shortcut.Modifiers | MOD_NOREPEAT, shortcut.Key);
            if (!registered) error = $"{shortcut} is already used by another program. Choose another shortcut.";
            ready.Set();
            if (!registered) return;
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.Message == WM_HOTKEY) Pressed?.Invoke();
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        })
        { IsBackground = true, Name = "Hotkey" };
        listener.Start();
        ready.Wait();
        if (error is null) thread = listener;
        return error;
    }

    public void Unregister()
    {
        if (thread is null) return;
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join();
        thread = null;
    }

    public void Dispose() => Unregister();

    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out Msg msg, IntPtr hWnd, uint min, uint max);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpeakForever.Interop;

/// <summary>
/// The clipboard, and finding out which window is in front. Speak Forever never sends key presses
/// to another program: you paste what it copies.
/// </summary>
public static partial class Native
{
    const int MaxTitleLength = 256, MaxPathLength = 32767;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x2;
    const int OpenAttempts = 10, OpenRetryMs = 20;
    static readonly IntPtr HWND_MESSAGE = -3;

    // Clipboard formats Windows reads to keep an item out of clipboard history (Win+V) and cloud sync.
    static readonly uint NoHistory = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    static readonly uint NoCloud = RegisterClipboardFormat("CanUploadToCloudClipboard");

    /// <summary>
    /// Puts text on the clipboard, kept out of clipboard history and cloud sync: it's a chat message
    /// waiting to be pasted, not something to keep. Returns null on success or an error description;
    /// <paramref name="version"/> is the clipboard's sequence number after, to tell later whether it's still ours.
    /// </summary>
    public static string? CopyText(string text, out uint version)
    {
        version = 0;
        // SetClipboardData needs an owner window; a message-only one is enough, and the text outlives it.
        var owner = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
        try
        {
            if (!OpenClipboardPatiently(owner)) return "Couldn't copy it: another program is using the clipboard. Try again.";
            try
            {
                EmptyClipboard();
                Span<byte> no = stackalloc byte[sizeof(int)]; // a DWORD 0
                no.Clear();
                if (!SetData(CF_UNICODETEXT, MemoryMarshal.AsBytes((text + "\0").AsSpan())))
                    return $"Couldn't copy it: Windows refused the clipboard (error {Marshal.GetLastPInvokeError()}).";
                SetData(NoHistory, no);
                SetData(NoCloud, no);
            }
            finally
            {
                CloseClipboard();
            }
            version = GetClipboardSequenceNumber();
            return null;
        }
        finally
        {
            if (owner != 0) DestroyWindow(owner);
        }
    }

    /// <summary>Empties the clipboard if it still holds what was copied at <paramref name="version"/>, and not something copied since.</summary>
    public static void ClearClipboard(uint version)
    {
        if (GetClipboardSequenceNumber() != version || !OpenClipboardPatiently(0)) return;
        EmptyClipboard();
        CloseClipboard();
    }

    /// <summary>The key is held down right now, in whichever program has focus.</summary>
    public static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>Another program may have the clipboard open for a moment; it's only ever briefly.</summary>
    static bool OpenClipboardPatiently(IntPtr owner)
    {
        for (int i = 0; i < OpenAttempts; i++)
        {
            if (OpenClipboard(owner)) return true;
            Thread.Sleep(OpenRetryMs);
        }
        return false;
    }

    static unsafe bool SetData(uint format, ReadOnlySpan<byte> data)
    {
        var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
        if (memory == 0) return false;
        var at = GlobalLock(memory);
        data.CopyTo(new Span<byte>((void*)at, data.Length));
        GlobalUnlock(memory);
        if (SetClipboardData(format, memory) != 0) return true; // the clipboard owns the memory now
        GlobalFree(memory);
        return false;
    }

    /// <summary>The foreground window's process name (without .exe), the .exe's full path ("" if it can't be read), and its title.</summary>
    public static unsafe (string Process, string Path, string Title) Foreground()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return ("", "", "");
        GetWindowThreadProcessId(h, out var pid);
        var path = ImagePath(pid);
        string name;
        if (path.Length > 0) name = System.IO.Path.GetFileNameWithoutExtension(path);
        else
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                name = p.ProcessName;
            }
            catch (ArgumentException)
            {
                name = "?"; // exited in between
            }
        }
        char* title = stackalloc char[MaxTitleLength];
        int length = GetWindowText(h, title, MaxTitleLength);
        return (name, path, new string(title, 0, Math.Max(0, length)));
    }

    /// <summary>A process's .exe path, or "" if it can't be read (it exited, or it's elevated and we're not).</summary>
    static unsafe string ImagePath(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return "";
        try
        {
            char* buffer = stackalloc char[MaxPathLength];
            uint length = MaxPathLength;
            return QueryFullProcessImageName(process, 0, buffer, ref length) ? new string(buffer, 0, (int)length) : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static unsafe partial int GetWindowText(IntPtr hWnd, char* text, int maxCount);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(IntPtr process, uint flags, char* name, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y,
        int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint format, IntPtr memory);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string name);

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalLock(IntPtr memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr memory);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalFree(IntPtr memory);
}

using System.Threading.Channels;
using SpeakForever.Configuration;

namespace SpeakForever.Logging;

/// <summary>
/// The app's log: the <see cref="Written"/> event (the app's Activity list), optionally the console, and,
/// once <see cref="ToFile"/> is called, a file. Callers include the UI and controller threads, so
/// the file is written by a background task and logging never waits on the disk.
/// </summary>
public static class Log
{
    static readonly Lock ConsoleGate = new();
    static readonly Channel<string> FileLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(30);
    static Task? fileWriter;
    static volatile bool toConsole, toFile;

    /// <summary>Raised for every line, on the thread that logged it.</summary>
    public static event Action<string, bool>? Written;

    public static void Info(string message) => Write(message, warning: false);
    public static void Warn(string message) => Write(message, warning: true);

    /// <summary>Also write to the console, for the CLI. The app has no console, so it doesn't.</summary>
    public static void ToConsole() => toConsole = true;

    /// <summary>Also write to a log file in the app folder; it starts fresh each run.</summary>
    public static void ToFile(string fileName)
    {
        if (fileWriter is not null) return;
        toFile = true; // lines queue from now on, while the file is opened
        fileWriter = Task.Run(() => WriteFileAsync(Path.Combine(AppPaths.Root, fileName)));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    /// <summary>
    /// Opens the log file, waiting while another copy still has it: after an update, or when
    /// reopened straight after closing, the old copy holds it for the seconds it takes to shut down.
    /// </summary>
    static async Task<StreamWriter?> OpenAsync(string path)
    {
        for (var waited = TimeSpan.Zero; ; waited += OpenRetryDelay)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                // Shared for reading, so the log can be tailed while the app runs.
                var file = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true));
                if (waited > TimeSpan.Zero) Info($"Waited {waited.TotalSeconds:F1} s for another copy of Speak Forever to let go of the log file.");
                return file;
            }
            catch (IOException) when (waited < OpenTimeout)
            {
                await Task.Delay(OpenRetryDelay).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                toFile = false;
                Warn($"Couldn't write the log file {path}: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>Writes out any queued lines and stops file logging; for shutdown.</summary>
    public static void Flush()
    {
        FileLines.Writer.TryComplete();
        // Blocking is fine here: it runs at exit, with no UI left to keep responsive.
        fileWriter?.Wait(TimeSpan.FromSeconds(2));
    }

    static async Task WriteFileAsync(string path)
    {
        if (await OpenAsync(path).ConfigureAwait(false) is not { } file) return;
        await using (file.ConfigureAwait(false))
        {
            // Everything queued is written, then flushed once: one disk write per burst of lines.
            while (await FileLines.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (FileLines.Reader.TryRead(out var line))
                    await file.WriteLineAsync(line).ConfigureAwait(false);
                await file.FlushAsync().ConfigureAwait(false);
            }
        }
    }

    static void Write(string message, bool warning)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        if (toConsole)
        {
            lock (ConsoleGate)
            {
                Console.ForegroundColor = warning ? ConsoleColor.Yellow : ConsoleColor.Gray;
                Console.WriteLine(line);
                Console.ResetColor();
            }
        }
        if (toFile) FileLines.Writer.TryWrite(warning ? "! " + line : line);
        Written?.Invoke(line, warning);
    }
}

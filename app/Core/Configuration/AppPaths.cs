namespace VoiceForever.Configuration;

/// <summary>Where the app and the CLI keep their shared state.</summary>
public static class AppPaths
{
    static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>%LOCALAPPDATA%\VoiceForever, or the VOICEFOREVER_DATA folder if that's set (tests use it to stay off real data).</summary>
    public static string Root { get; } = Environment.GetEnvironmentVariable("VOICEFOREVER_DATA") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(LocalAppData, "VoiceForever");
    public static string Config { get; } = Path.Combine(Root, "voiceforever.json");
    public static string Models { get; } = Path.Combine(Root, "models");
    public static string Benchmark { get; } = Path.Combine(Root, "benchmark");

    /// <summary>The folder from before the rename to Voice Forever.</summary>
    public static string OldRoot { get; } = Path.Combine(LocalAppData, "ForeverVoice");

    /// <summary>
    /// Moves the models, settings and benchmark clips across from the old name. Item by item, so
    /// one locked file (a log another program has open) can't strand the rest; anything left
    /// behind is picked up on a later launch. Call once at startup, before logging to a file.
    /// </summary>
    public static void MigrateFromOldName()
    {
        if (!Directory.Exists(OldRoot)) return;
        Directory.CreateDirectory(Root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(OldRoot))
        {
            var name = Path.GetFileName(entry);
            // The old settings win over a default file an interrupted migration may have created.
            bool isOldConfig = name.Equals("forevervoice.json", StringComparison.OrdinalIgnoreCase);
            var target = Path.Combine(Root, isOldConfig ? "voiceforever.json" : name);
            try
            {
                if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                    File.Delete(entry); // logs start fresh each run anyway
                else if (Directory.Exists(entry))
                {
                    if (!Directory.Exists(target)) MoveDirectory(entry, target);
                }
                else if (isOldConfig || !File.Exists(target))
                {
                    File.Move(entry, target, overwrite: isOldConfig);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // In use; left for the next launch.
            }
        }
        try
        {
            Directory.Delete(OldRoot); // only succeeds once it's empty
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A rename where possible. Where Windows refuses one (folders it treats as another volume,
    /// such as redirected or virtualised ones), copy then delete, as Explorer does.
    /// </summary>
    static void MoveDirectory(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        try
        {
            CopyDirectory(from, to);
        }
        catch
        {
            // Don't leave half a copy that a later launch would take for the real thing.
            try { Directory.Delete(to, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        Directory.Delete(from, recursive: true);
    }

    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}

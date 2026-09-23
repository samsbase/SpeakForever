namespace SpeakForever.Configuration;

/// <summary>Where the app and the CLI keep their shared state.</summary>
public static class AppPaths
{
    static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>%LOCALAPPDATA%\SpeakForever, or the SPEAKFOREVER_DATA folder if that's set (tests use it to stay off real data).</summary>
    public static string Root { get; } = Environment.GetEnvironmentVariable("SPEAKFOREVER_DATA") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(LocalAppData, "SpeakForever");
    public static string Config { get; } = Path.Combine(Root, "speakforever.json");
    public static string Models { get; } = Path.Combine(Root, "models");
    public static string Benchmark { get; } = Path.Combine(Root, "benchmark");

    /// <summary>The app's earlier names (newest first) and what each called its settings file.</summary>
    static readonly (string Folder, string ConfigFile)[] OldNames =
    [
        ("VoiceForever", "voiceforever.json"),
        ("ForeverVoice", "forevervoice.json"),
    ];

    /// <summary>The data folders from before the rename to Speak Forever, newest first.</summary>
    public static IReadOnlyList<string> OldRoots { get; } = [.. OldNames.Select(n => Path.Combine(LocalAppData, n.Folder))];

    /// <summary>
    /// Moves the models, settings and benchmark clips across from the old names. Item by item, so
    /// one locked file (a log another program has open) can't strand the rest; anything left
    /// behind is picked up on a later launch. Call once at startup, before logging to a file.
    /// </summary>
    public static void MigrateFromOldName()
    {
        for (int i = 0; i < OldNames.Length; i++)
        {
            var oldRoot = OldRoots[i];
            if (!Directory.Exists(oldRoot)) continue;
            Directory.CreateDirectory(Root);
            foreach (var entry in Directory.EnumerateFileSystemEntries(oldRoot))
            {
                var name = Path.GetFileName(entry);
                bool isOldConfig = name.Equals(OldNames[i].ConfigFile, StringComparison.OrdinalIgnoreCase);
                var target = Path.Combine(Root, isOldConfig ? Path.GetFileName(Config) : name);
                // The newest old settings win over a default file an interrupted migration may have created.
                bool overwrite = isOldConfig && i == 0;
                try
                {
                    if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || name == "controller.lock")
                        File.Delete(entry); // logs start fresh each run anyway
                    else if (Directory.Exists(entry))
                    {
                        if (!Directory.Exists(target)) MoveDirectory(entry, target);
                    }
                    else if (overwrite || !File.Exists(target))
                    {
                        File.Move(entry, target, overwrite);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // In use; left for the next launch.
                }
            }
            try
            {
                Directory.Delete(oldRoot); // only succeeds once it's empty
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
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

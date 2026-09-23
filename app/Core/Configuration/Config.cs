using System.Text.Encodings.Web;
using System.Text.Json;

namespace VoiceForever.Configuration;

/// <summary>
/// The user's settings, kept as hand-editable JSON in <see cref="AppPaths.Config"/>. The app reads
/// and changes it from the UI thread and the controller thread reads it, so writes are whole
/// values (a string or an int), never partial updates.
/// </summary>
public sealed class Config
{
    /// <summary>
    /// Game client process names, without ".exe". The WoW: Forever beta runs as WowB (the
    /// _classic_beta_ flavor); the live release may use a different name.
    /// </summary>
    public string[] ProcessNames { get; set; } = ["WowB"];

    /// <summary>XInput slot 0-3, or -1 for the first controller found.</summary>
    public int ControllerSlot { get; set; } = -1;

    // The app follows WoW's gamepad chat panel from these buttons, so it only ever types into an
    // open chat box. They mirror the game's defaults; change them if you rebind the game.

    /// <summary>WoW's chord that opens the chat panel.</summary>
    public string OpenChatChord { get; set; } = "LB+RB+DOWN";

    /// <summary>Starts a dictation while the chat panel is open. In the panel WoW only uses it to toggle tooltips.</summary>
    public string DictateChord { get; set; } = "RS";

    public string SendChord { get; set; } = "A";
    public string BackChord { get; set; } = "B";

    /// <summary>The panel's Chat Channels and Tab Settings menus, which reuse A and B.</summary>
    public string[] MenuChords { get; set; } = ["X", "Y"];

    /// <summary>WoW's radial menu button (the Menu / "three lines" button). Picking Chat there opens chat too.</summary>
    public string RadialMenuChord { get; set; } = "START";

    /// <summary>
    /// Optional system-wide shortcut, e.g. "Ctrl+Shift+Space": dictates into whatever text box has
    /// focus, in any program, like Win+H. Off (null) by default.
    /// </summary>
    public string? KeyboardShortcut { get; set; }

    /// <summary>Pause after the dictate button before recording, so the start beep isn't recorded.</summary>
    public int DelayMs { get; set; } = 150;

    /// <summary>The model last used; empty until one is. Set by the app's model list.</summary>
    public string ModelPath { get; set; } = "";

    /// <summary>Whisper language code, or "auto" to detect it each time.</summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Words Whisper should expect. Game names are its weak spot: without this it hears
    /// "Iron Fudge" and "Dead Minds". Add your guild, friends' names or anything it mishears.
    /// </summary>
    public string Prompt { get; set; } =
        "World of Warcraft chat. Ironforge, Stormwind, Orgrimmar, Undercity, Darnassus, Thunder Bluff, " +
        "Deadmines, Westfall, Elwynn Forest, Stranglethorn, Molten Core, Onyxia, Blackrock, Hyjal, Skyborne.";

    public bool UseGpu { get; set; } = true;

    /// <summary>
    /// Beam search width; 5 matches OpenAI's reference transcriber, 1 decodes greedily. On turbo the
    /// benchmark found greedy just as accurate, faster, and lighter at peak.
    /// </summary>
    public int BeamSize { get; set; } = 1;

    /// <summary>Recording device index, or -1 for the Windows default microphone.</summary>
    public int MicDevice { get; set; } = -1;

    /// <summary>A pause this long ends the recording. Pressing the dictate button again ends it straight away.</summary>
    public int SilenceMs { get; set; } = 1500;

    /// <summary>Give up if no speech starts within this long.</summary>
    public int NoSpeechTimeoutSeconds { get; set; } = 6;

    /// <summary>Only a guard against a microphone that never goes quiet; far longer than a chat message.</summary>
    public int MaxSeconds { get; set; } = 120;

    /// <summary>How far above the measured background noise counts as speech.</summary>
    public double SpeechThresholdDb { get; set; } = 10;

    public bool Sounds { get; set; } = true;

    public bool IsGame(string process) => ProcessNames.Contains(process, StringComparer.OrdinalIgnoreCase);

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // A hand edit that sets a required value to null is an error, not a crash later on.
        RespectNullableAnnotations = true,
        // Keep "+" and "\" readable; this file is meant to be edited by hand.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // The app, the controller thread and model loads can all save; one write at a time.
    static readonly SemaphoreSlim SaveGate = new(1, 1);

    /// <summary>Reads the settings, or starts from the defaults the first time.</summary>
    /// <exception cref="JsonException">The file isn't valid settings JSON.</exception>
    public static async Task<Config> LoadOrCreateAsync(CancellationToken ct = default)
    {
        var cfg = new Config();
        if (File.Exists(AppPaths.Config))
        {
            var stream = new FileStream(AppPaths.Config, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            await using (stream.ConfigureAwait(false))
                cfg = await JsonSerializer.DeserializeAsync<Config>(stream, Json, ct).ConfigureAwait(false) ?? cfg;
        }
        if (cfg.ModelPath.StartsWith(AppPaths.OldRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            cfg.ModelPath = AppPaths.Root + cfg.ModelPath[AppPaths.OldRoot.Length..];
        await cfg.SaveAsync(ct).ConfigureAwait(false); // writes out settings added since the file was created, with their defaults
        return cfg;
    }

    /// <summary>
    /// Writes the settings beside the real file and swaps it in, so a crash mid-write can't leave
    /// a half-written file that fails to load next time.
    /// </summary>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        await SaveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var temp = AppPaths.Config + ".tmp";
            var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
            await using (stream.ConfigureAwait(false))
                await JsonSerializer.SerializeAsync(stream, this, Json, ct).ConfigureAwait(false);
            File.Move(temp, AppPaths.Config, overwrite: true);
        }
        finally
        {
            SaveGate.Release();
        }
    }
}

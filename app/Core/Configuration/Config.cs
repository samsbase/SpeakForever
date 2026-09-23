using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeakForever.Configuration;

/// <summary>
/// The user's settings, kept as hand-editable JSON in <see cref="AppPaths.Config"/>. Immutable: a
/// change makes a new Config (<c>with</c>) that <see cref="Engine.UpdateConfigAsync"/> swaps in whole,
/// so the UI, controller and dictation threads always see one consistent set of settings.
/// </summary>
public sealed record Config
{
    /// <summary>
    /// The WoW: Forever folder, the one with the game's .exe in it: found on first run, or chosen
    /// on the Settings tab. The app only types into a program running from here.
    /// </summary>
    public string GameFolder { get; init; } = "";

    /// <summary>
    /// Game process names, without ".exe": what counts as the game while <see cref="GameFolder"/>
    /// isn't set. The WoW: Forever beta runs as WowB.
    /// </summary>
    public IReadOnlyList<string> ProcessNames { get; init; } = ["WowB"];

    /// <summary>Which controller to use when several are connected: 0 for the first, up to 3, or -1 for whichever is found first.</summary>
    public int ControllerSlot { get; init; } = -1;

    // The app follows WoW's gamepad chat panel from these buttons, so it only ever types into an
    // open chat box. They mirror the game's defaults; change them if you rebind the game.

    /// <summary>WoW's chord that opens the chat panel.</summary>
    public string OpenChatChord { get; init; } = "LB+RB+DOWN";

    /// <summary>Starts a dictation while the chat panel is open. In the panel WoW only uses it to toggle tooltips.</summary>
    public string DictateChord { get; init; } = "RS";

    public string SendChord { get; init; } = "A";
    public string BackChord { get; init; } = "B";

    /// <summary>The panel's Chat Channels and Tab Settings menus, which reuse A and B.</summary>
    public IReadOnlyList<string> MenuChords { get; init; } = ["X", "Y"];

    /// <summary>WoW's radial menu button (the Menu / "three lines" button). Picking Chat there opens chat too.</summary>
    public string RadialMenuChord { get; init; } = "START";

    /// <summary>
    /// Starts the message over while chat is open: deletes what was dictated into the chat box and
    /// listens again. WoW's gamepad chat box has no way to delete text, so this is how to fix a mistake.
    /// </summary>
    public string RedoChord { get; init; } = "DOWN";

    /// <summary>
    /// Optional system-wide shortcut, e.g. "Ctrl+Shift+Space": dictates into whatever text box has
    /// focus, in any program, like Win+H. Off (null) by default.
    /// </summary>
    public string? KeyboardShortcut { get; init; }

    /// <summary>Pause after the dictate button before recording, so the start beep isn't recorded.</summary>
    public int DelayMs { get; init; } = 150;

    /// <summary>The model last used; empty until one is. Set by the app's model list.</summary>
    public string ModelPath { get; init; } = "";

    /// <summary>Whisper language code, or "auto" to detect it each time.</summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// Words Whisper should expect. Game names are its weak spot: without this it hears
    /// "Iron Fudge" and "Dead Minds". Only names it would otherwise get wrong are worth their
    /// place: Whisper reads just the last 224 tokens (this default is 211) and silently drops
    /// the start of anything longer, so the most important names come last.
    /// </summary>
    public string Prompt { get; init; } = DefaultPrompt;

    internal const string DefaultPrompt =
        "World of Warcraft: Forever chat. LFG, LFM, WTS, DPS, OOM, AoE, rez. " +
        "Innervate, Soulstone, Arcane Intellect, Rage of the Farseer, Maelstrom Weapon, Templar's Bulwark, " +
        "Litany of Light, Frostfire Bolt, Mutilate. " +
        "Ironforge, Orgrimmar, Darnassus, Shen'dralas, Zephras Isle, Skyborne. " +
        "Deadmines, Shadowfang Keep, Blackfathom Deeps, Gnomeregan, Razorfen, Uldaman, Zul'Farrak, Maraudon, " +
        "Stratholme, Scholomance. " +
        "Hall of Thanes, Ruins of Lordaeron, Dalaran, Drowned City, Krol'dok Stronghold, Alcaz Prison, " +
        "Blackmaw Hold, Shaper's Terrace, Barrow Deeps, Hyjal Summit, Onyxia's Lair.";

    /// <summary>
    /// Puts WoW: Forever names back where Whisper wrote something that sounds like one but isn't a
    /// real word ("Stratham" becomes Stratholme). Real words are never changed.
    /// </summary>
    public bool CorrectNames { get; init; } = true;

    /// <summary>Earlier versions' default prompts: still unchanged in a settings file, they move to the current one.</summary>
    static readonly string[] OldDefaultPrompts =
    [
        "World of Warcraft chat. Ironforge, Stormwind, Orgrimmar, Undercity, Darnassus, Thunder Bluff, " +
        "Deadmines, Westfall, Elwynn Forest, Stranglethorn, Molten Core, Onyxia, Blackrock, Hyjal, Skyborne.",
    ];

    /// <summary>
    /// Vulkan GPU, or false for CPU only. Takes effect after a restart: whisper.cpp's native library
    /// is chosen once per process, when the first model loads.
    /// </summary>
    public bool UseGpu { get; init; } = true;

    /// <summary>
    /// Beam search width; 5 matches OpenAI's reference transcriber, 1 decodes greedily. On turbo the
    /// benchmark found greedy just as accurate, faster, and lighter at peak.
    /// </summary>
    public int BeamSize { get; init; } = 1;

    /// <summary>Recording device index, or -1 for the Windows default microphone.</summary>
    public int MicDevice { get; init; } = -1;

    /// <summary>A pause this long ends the recording. Pressing the dictate button again ends it straight away.</summary>
    public int SilenceMs { get; init; } = 1500;

    /// <summary>Give up if no speech starts within this long.</summary>
    public int NoSpeechTimeoutSeconds { get; init; } = 6;

    /// <summary>Only a guard against a microphone that never goes quiet; far longer than a chat message.</summary>
    public int MaxSeconds { get; init; } = 120;

    /// <summary>How far above the measured background noise counts as speech.</summary>
    public double SpeechThresholdDb { get; init; } = 10;

    public bool Sounds { get; init; } = true;

    /// <summary>
    /// Shows "Listening" at the top of the screen, over the game, while you speak, and says when a
    /// message is too long for the chat box.
    /// </summary>
    public bool ShowOverlay { get; init; } = true;

    /// <summary>Checks for updates on GitHub at launch and every few hours.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>
    /// The foreground program is the game: its .exe is in <see cref="GameFolder"/>. Without a
    /// folder, or when the .exe's path can't be read, its process name is one of <see cref="ProcessNames"/>.
    /// </summary>
    public bool IsGame(string process, string exePath) =>
        GameFolder.Length > 0 && exePath.Length > 0
            ? string.Equals(Path.GetDirectoryName(exePath), Path.TrimEndingDirectorySeparator(GameFolder), StringComparison.OrdinalIgnoreCase)
            : ProcessNames.Contains(process, StringComparer.OrdinalIgnoreCase);

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        // A misspelt setting is an error rather than silently ignored. Comments are refused too
        // (the default): every save rewrites the file, so they would quietly disappear.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // A hand edit that sets a required value to null is an error, not a crash later on.
        RespectNullableAnnotations = true,
        // Keep "+" and "\" readable; this file is meant to be edited by hand.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Saves from anywhere happen one at a time.
    static readonly SemaphoreSlim SaveGate = new(1, 1);

    /// <summary>Checks every value is usable; the file is edited by hand, so this is the trust boundary.</summary>
    /// <exception cref="FormatException">A value is missing or out of range; the message names it.</exception>
    public Config Validated()
    {
        if (ProcessNames.Count == 0 || ProcessNames.Any(string.IsNullOrWhiteSpace))
            throw new FormatException($"{nameof(ProcessNames)} needs at least one game process name.");
        Range(ControllerSlot, -1, 3, nameof(ControllerSlot));
        Range(MicDevice, -1, 31, nameof(MicDevice));
        Range(DelayMs, 0, 2000, nameof(DelayMs));
        Range(BeamSize, 1, 16, nameof(BeamSize));
        Range(SilenceMs, 300, 10_000, nameof(SilenceMs));
        Range(NoSpeechTimeoutSeconds, 1, 60, nameof(NoSpeechTimeoutSeconds));
        Range(MaxSeconds, 5, 600, nameof(MaxSeconds));
        Range(SpeechThresholdDb, 1, 40, nameof(SpeechThresholdDb));
        if (string.IsNullOrWhiteSpace(Language)) throw new FormatException($"{nameof(Language)} needs a Whisper language code, or \"auto\".");
        return this;

        static void Range(double value, double min, double max, string name)
        {
            if (value < min || value > max) throw new FormatException($"{name} is {value}; it must be from {min} to {max}.");
        }
    }

    /// <summary>Reads and checks the settings, or starts from the defaults the first time.</summary>
    /// <exception cref="JsonException">The file isn't valid settings JSON, or names a setting that doesn't exist.</exception>
    /// <exception cref="FormatException">A value is out of range.</exception>
    public static async Task<Config> LoadOrCreateAsync(CancellationToken ct = default)
    {
        var cfg = new Config();
        if (File.Exists(AppPaths.Config))
        {
            var stream = new FileStream(AppPaths.Config, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            await using (stream.ConfigureAwait(false))
                cfg = await JsonSerializer.DeserializeAsync<Config>(stream, Json, ct).ConfigureAwait(false) ?? cfg;
        }
        foreach (var oldRoot in AppPaths.OldRoots)
        {
            if (cfg.ModelPath.StartsWith(oldRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                cfg = cfg with { ModelPath = AppPaths.Root + cfg.ModelPath[oldRoot.Length..] };
        }
        if (OldDefaultPrompts.Contains(cfg.Prompt, StringComparer.Ordinal)) cfg = cfg with { Prompt = DefaultPrompt };
        cfg.Validated();
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

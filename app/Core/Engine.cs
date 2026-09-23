using System.Diagnostics;
using System.Runtime.CompilerServices;
using SpeakForever.Configuration;
using SpeakForever.Dictation;
using SpeakForever.Game;
using SpeakForever.Input;
using SpeakForever.Interop;
using SpeakForever.Logging;
using SpeakForever.Speech;

[assembly: InternalsVisibleTo("SpeakForever.Core.Tests")]

namespace SpeakForever;

/// <summary>
/// Owns the controller loop, the loaded speech model and the dictation session. The WinUI app
/// and the CLI are both thin shells around this.
/// </summary>
public sealed class Engine : IAsyncDisposable
{
    const int PollMs = 8;      // Windows' timer tick makes this ~15 ms in practice: still under a frame
    const int RescanMs = 1000; // look for a new controller once a second

    readonly HotkeyListener hotkey = new();
    readonly RadialMenu radialMenu = new();
    readonly ChatPanel chat = new();
    readonly Session session;
    readonly SemaphoreSlim configGate = new(1, 1);
    volatile Config config;
    volatile ControllerBindings bindings;
    volatile ChordRecorder? recorder;
    TaskCompletionSource<Chord>? recorded;
    volatile int controllerSlot = -1;
    volatile Thread? thread;
    Transcriber? transcriber;
    CancellationTokenSource? stopping;
    FileStream? controllerLock;

    /// <exception cref="FormatException">A chord in the settings doesn't parse.</exception>
    public Engine(Config config)
    {
        this.config = config;
        bindings = ControllerBindings.From(config);
        session = new Session(() => this.config, () => transcriber, (text, took, seconds) => Transcribed?.Invoke(text, took, seconds),
            phase => PhaseChanged?.Invoke(phase));
        hotkey.Pressed += () =>
        {
            if (IsRunning) session.Start(this.config.KeyboardShortcut ?? "Shortcut", anyWindow: true);
        };
    }

    /// <summary>Raised on a background thread whenever any of the state properties change.</summary>
    public event Action? StateChanged;

    /// <summary>Raised on a background thread with each dictation: text, transcription time, audio seconds.</summary>
    public event Action<string, TimeSpan, double>? Transcribed;

    /// <summary>Raised on a background thread as a dictation starts listening, transcribes, and finishes.</summary>
    public event Action<DictationPhase>? PhaseChanged;

    /// <summary>The current settings. A snapshot: change them with <see cref="UpdateConfigAsync"/>.</summary>
    public Config Config => config;

    public bool IsRunning => thread is not null;
    /// <summary>Which connected controller is in use (0 for the first), or -1 when there's none.</summary>
    public int ControllerSlot => controllerSlot;

    /// <summary>The controller in use, such as "Xbox Series X Controller"; the last one, once it's gone.</summary>
    public string? ControllerName { get; private set; }

    /// <summary>Whose button icons to show: the controller in use, or the last one there was.</summary>
    public ButtonStyle ButtonStyle { get; private set; }
    public bool IsLoadingModel => LoadingModel is not null;
    public string? LoadingModel { get; private set; }
    public string? LoadedModel { get; private set; }
    public string ModelStatus { get; private set; } = "No speech model loaded";

    /// <summary>The model last used, if its file was deleted since; set by <see cref="StartingModel"/>.</summary>
    public string? RemovedModel { get; private set; }

    /// <summary>The chat panel is open with its text box focused (not in one of its menus).</summary>
    public bool ChatOpen => chat.InTextBox;

    /// <summary>Why the last Start() refused, or null.</summary>
    public string? StartError { get; private set; }

    /// <summary>Why the keyboard shortcut couldn't be registered, or null.</summary>
    public string? KeyboardError { get; private set; }

    /// <summary>The settings point at a folder with WoW: Forever in it. Set by <see cref="FindGameAsync"/>.</summary>
    public bool GameFound { get; private set; }

    // ---- Settings ---------------------------------------------------------------------------

    /// <summary>
    /// Changes the settings and saves them. Changes apply one at a time, each to the latest
    /// settings, so two made at once can't lose either; readers see the old or the new, never a mix.
    /// </summary>
    /// <exception cref="FormatException">The change puts a value out of range; nothing is changed.</exception>
    public async Task UpdateConfigAsync(Func<Config, Config> change, CancellationToken ct = default)
    {
        await configGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var updated = change(config).Validated();
            config = updated;
            await updated.SaveAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            configGate.Release();
        }
    }

    // ---- The game ---------------------------------------------------------------------------

    /// <summary>
    /// Checks the settings still point at WoW: Forever, and if not (the first run, or it has moved)
    /// looks for it and saves what it finds. Returns the folder, or null if it isn't on this PC.
    /// </summary>
    public async Task<string?> FindGameAsync(bool searchAgain = false, CancellationToken ct = default)
    {
        var saved = config.GameFolder;
        // Reads the registry and lists folders: off the caller's thread.
        var found = !searchAgain && await Task.Run(() => GameInstall.HasGame(saved), ct).ConfigureAwait(false)
            ? saved
            : await Task.Run(GameInstall.Detect, ct).ConfigureAwait(false);
        GameFound = found is not null;
        if (found is null)
            Log.Warn(saved.Length > 0 ? $"WoW: Forever is no longer in {saved}. Show Speak Forever where it is on the Settings tab." : "Couldn't find WoW: Forever. Show Speak Forever where it's installed on the Settings tab.");
        else if (!string.Equals(found, saved, StringComparison.OrdinalIgnoreCase))
        {
            await UpdateConfigAsync(c => c with { GameFolder = found }, ct).ConfigureAwait(false);
            Log.Info($"Found WoW: Forever in {found}.");
        }
        Changed();
        return found;
    }

    /// <summary>Points the app at WoW: Forever: its folder, its .exe, or the World of Warcraft folder. Returns why not, or null.</summary>
    public async Task<string?> SetGameFolderAsync(string path, CancellationToken ct = default)
    {
        var folder = await Task.Run(() => GameInstall.Resolve(path), ct).ConfigureAwait(false);
        if (folder is null) return $"WoW: Forever isn't in {path}. Choose the folder with WowB.exe in it; for the beta, that's World of Warcraft\\_classic_beta_.";
        await UpdateConfigAsync(c => c with { GameFolder = folder }, ct).ConfigureAwait(false);
        GameFound = true;
        Log.Info($"WoW: Forever is in {folder}.");
        Changed();
        return null;
    }

    // ---- Controller bindings ----------------------------------------------------------------

    /// <summary>
    /// Records the next chord pressed on the controller instead of acting on it. Null if nothing
    /// was pressed before the timeout or cancellation. Needs the controller loop running.
    /// </summary>
    /// <exception cref="InvalidOperationException">The controller loop isn't running.</exception>
    public async Task<Chord?> CaptureChordAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!IsRunning) throw new InvalidOperationException("Set Speak Forever to Active first.");
        session.ChatClosing("Changing buttons");
        chat.Close();
        var done = recorded = new TaskCompletionSource<Chord>(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder = new ChordRecorder();
        try
        {
            return await done.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            return null;
        }
        finally
        {
            recorder = null;
        }
    }

    /// <summary>Changes a binding and saves it. Returns why it was refused, or null.</summary>
    public async Task<string?> SetBindingAsync(BindingKind which, Chord chord, CancellationToken ct = default)
    {
        var b = bindings;
        (string Name, Chord Chord)[] others =
        [
            ("opening chat", b.OpenChat), ("dictating", b.Dictate), ("the chat panel's Send", b.Send),
            ("the chat panel's Back", b.Back), .. b.Menus.Select(m => ("the chat panel's menus", m)),
            ("the radial menu", b.Radial),
        ];
        var mine = which == BindingKind.OpenChat ? b.OpenChat : b.Dictate;
        foreach (var (name, other) in others)
            if (!other.SameButtons(mine) && other.SameButtons(chord))
                return $"{chord.Text} is already used for {name}.";

        await UpdateConfigAsync(c => which == BindingKind.OpenChat ? c with { OpenChatChord = chord.Text } : c with { DictateChord = chord.Text }, ct)
            .ConfigureAwait(false);
        bindings = ControllerBindings.From(config);
        chat.Close();
        Log.Info($"{(which == BindingKind.OpenChat ? "Open chat" : "Dictate")} is now {chord.Text}.");
        Changed();
        return null;
    }

    // ---- Keyboard shortcut ------------------------------------------------------------------

    /// <summary>Sets (or with null, turns off) the keyboard shortcut. Returns why it was refused, or null.</summary>
    public async Task<string?> SetKeyboardShortcutAsync(Shortcut? shortcut, CancellationToken ct = default)
    {
        var text = shortcut?.ToString();
        hotkey.Unregister();
        if (IsRunning)
        {
            RegisterKeyboardShortcut(text);
            if (KeyboardError is { } error)
            {
                RegisterKeyboardShortcut(config.KeyboardShortcut); // put the old one back
                return error;
            }
        }
        else KeyboardError = null; // registered for real when the app is next set to Active
        await UpdateConfigAsync(c => c with { KeyboardShortcut = text }, ct).ConfigureAwait(false);
        if (shortcut is null) Log.Info("Keyboard shortcut is off.");
        Changed();
        return null;
    }

    /// <summary>While the app records a new shortcut, the old one mustn't fire.</summary>
    public void SuspendKeyboardShortcut() => hotkey.Unregister();

    public void ResumeKeyboardShortcut()
    {
        if (IsRunning) RegisterKeyboardShortcut(config.KeyboardShortcut);
    }

    void RegisterKeyboardShortcut(string? text)
    {
        KeyboardError = null;
        if (text is null) return;
        try
        {
            KeyboardError = hotkey.Register(Shortcut.Parse(text));
        }
        catch (FormatException e)
        {
            KeyboardError = e.Message;
        }
        if (KeyboardError is not null) Log.Warn(KeyboardError);
        else Log.Info($"Keyboard shortcut {text} is on.");
    }

    // ---- Speech models ----------------------------------------------------------------------

    /// <summary>
    /// The model to load at launch: the one last used if it's still there, otherwise the best one
    /// downloaded (catalog order, then any the user added), otherwise null.
    /// </summary>
    public string? StartingModel()
    {
        var saved = config.ModelPath;
        var installed = ModelCatalog.Installed();
        if (installed.FirstOrDefault(p => string.Equals(p, saved, StringComparison.OrdinalIgnoreCase)) is { } found) return found;
        var fallback = ModelCatalog.All.Where(m => m.IsInstalled).Select(m => m.LocalPath).FirstOrDefault()
                       ?? (installed.Count > 0 ? installed[0] : null);
        if (saved.Length > 0)
        {
            RemovedModel = ModelCatalog.DisplayName(saved);
            Log.Warn(fallback is null
                ? $"{RemovedModel} is missing from the models folder. Download a speech model to dictate."
                : $"{RemovedModel} is missing from the models folder, so Speak Forever is using {ModelCatalog.DisplayName(fallback)} instead.");
        }
        return fallback;
    }

    /// <summary>Loads a model and swaps it in; the old one is released once it's idle. Failures are logged, not thrown.</summary>
    public async Task LoadModelAsync(string path, CancellationToken ct = default)
    {
        var name = ModelCatalog.DisplayName(path);
        LoadingModel = path;
        ModelStatus = $"Loading {name}…";
        Changed();
        try
        {
            var started = Stopwatch.GetTimestamp();
            var loaded = await Transcriber.LoadAsync(config, path, ct).ConfigureAwait(false);
            var old = Interlocked.Exchange(ref transcriber, loaded);
            LoadedModel = path;
            ModelStatus = $"{name} is ready, running on {Transcriber.RuntimeInfo}. Loaded in {Stopwatch.GetElapsedTime(started).TotalSeconds:F1} s.";
            Log.Info(ModelStatus);
            if (config.ModelPath != path) await UpdateConfigAsync(c => c with { ModelPath = path }, ct).ConfigureAwait(false);
            if (old is not null) await old.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            ModelStatus = $"Couldn't load {name}: {e.Message}";
            Log.Warn(ModelStatus);
        }
        finally
        {
            LoadingModel = null;
            Changed();
        }
    }

    /// <summary>Deletes a downloaded model. Returns why it couldn't, or null. The one in use can't be deleted.</summary>
    public string? DeleteModel(string path)
    {
        if (string.Equals(path, LoadedModel, StringComparison.OrdinalIgnoreCase))
            return "That model is in use. Switch to another one first.";
        try
        {
            File.Delete(path);
            Log.Info($"Deleted {Path.GetFileName(path)}.");
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't delete it: {e.Message}";
        }
    }

    /// <summary>One dictation without the game, for comparing models. Null if nobody spoke.</summary>
    /// <exception cref="InvalidOperationException">No model is loaded.</exception>
    public async Task<(string Text, TimeSpan Took, double Seconds)?> TestMicAsync(CancellationToken finish = default, CancellationToken ct = default)
    {
        var model = transcriber ?? throw new InvalidOperationException("No speech model is loaded.");
        var cfg = config;
        try
        {
            Cue.Start(cfg);
            PhaseChanged?.Invoke(DictationPhase.Listening);
            await Task.Delay(cfg.DelayMs, ct).ConfigureAwait(false);
            var audio = await Recorder.RecordUtteranceAsync(cfg, finish, ct).ConfigureAwait(false);
            if (audio is null) return null;
            Cue.Heard(cfg);
            PhaseChanged?.Invoke(DictationPhase.Transcribing);
            var (text, took) = await model.TimedAsync(audio, ct).ConfigureAwait(false);
            return (text, took, audio.Length / (double)Recorder.SampleRate);
        }
        finally
        {
            PhaseChanged?.Invoke(DictationPhase.Idle);
        }
    }

    /// <summary>Transcribes samples with the loaded model, for the CLI's file test.</summary>
    /// <exception cref="InvalidOperationException">No model is loaded.</exception>
    public Task<(string Text, TimeSpan Took)> TranscribeAsync(float[] samples, CancellationToken ct = default) =>
        (transcriber ?? throw new InvalidOperationException("No speech model is loaded.")).TimedAsync(samples, ct);

    // ---- The controller loop ----------------------------------------------------------------

    /// <summary>
    /// Starts watching the controller; false if another Speak Forever already is. In probe mode
    /// it only logs presses, so it doesn't need the controller to itself.
    /// </summary>
    public bool Start(bool probe = false)
    {
        if (IsRunning) return true;
        if (!probe && !TryTakeControllerLock())
        {
            StartError = "Another copy of Speak Forever is already running. Close it first, or both would type into the game.";
            Log.Warn(StartError);
            Changed();
            return false;
        }
        StartError = null;
        var cts = stopping = new CancellationTokenSource();
        // A dedicated thread, not a timer or a task: XInput has no events, so this is a blocking
        // poll for the app's lifetime, which would otherwise pin a thread-pool thread.
        var poller = new Thread(() => Poll(probe, cts.Token)) { IsBackground = true, Name = "Controller" };
        thread = poller;
        poller.Start();
        Log.Info(probe ? "Probe mode: logging presses only, nothing is recorded." : "Watching the controller.");
        if (!probe) RegisterKeyboardShortcut(config.KeyboardShortcut);
        Changed();
        return true;
    }

    /// <summary>
    /// One controller watcher at a time, or both would type every message. An exclusively opened
    /// file rather than a named semaphore or mutex: Windows closes it if the process dies, and any
    /// thread can release it.
    /// </summary>
    bool TryTakeControllerLock()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            controllerLock = new FileStream(Path.Combine(AppPaths.Root, "controller.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void Stop()
    {
        if (thread is not { } poller) return;
        stopping!.Cancel();
        poller.Join();
        thread = null;
        stopping.Dispose();
        stopping = null;
        session.CancelAll();
        hotkey.Unregister();
        chat.Close();
        radialMenu.Reset();
        controllerLock?.Dispose();
        controllerLock = null;
        controllerSlot = -1;
        Log.Info("Stopped watching the controller.");
        Changed();
    }

    void Poll(bool probe, CancellationToken ct)
    {
        GamepadReader reader;
        try
        {
            reader = new GamepadReader(Path.Combine(AppContext.BaseDirectory, "gamecontrollerdb.txt"));
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException)
        {
            Log.Warn($"Couldn't read controllers: {e.Message} The keyboard shortcut still works.");
            return;
        }
        using var _ = reader;
        uint prev = 0;
        long nextScan = 0;
        while (!ct.IsCancellationRequested)
        {
            PadState? read = reader.Read();
            if (controllerSlot >= 0 && read is null)
            {
                Log.Warn("Controller disconnected.");
                controllerSlot = -1;
                Changed();
            }
            if (controllerSlot < 0 && Environment.TickCount64 >= nextScan)
            {
                nextScan = Environment.TickCount64 + RescanMs;
                if (reader.TryOpen(config.ControllerSlot))
                {
                    controllerSlot = Math.Max(0, config.ControllerSlot);
                    ControllerName = reader.Name;
                    ButtonStyle = reader.Style;
                    Log.Info($"Controller connected: {reader.Name}.");
                    read = reader.Read();
                    Changed();
                }
            }

            uint cur = read?.Buttons ?? 0;
            if (cur != prev)
            {
                if (recorder is { } r)
                {
                    if (r.Step(prev, cur) is { } chord) recorded?.TrySetResult(chord);
                    prev = cur;
                    continue;
                }
                if (probe && (cur & ~prev) != 0)
                {
                    var fg = Native.Foreground();
                    Log.Info($"Held: {Gamepad.Describe(cur)} | foreground: {fg.Process} \"{fg.Title}\"");
                }
                OnButtons(prev, cur, probe);
                prev = cur;
            }
            if (radialMenu.IsOpen && read is { } pad) OnRightStick(pad.RightX, pad.RightY);
            Thread.Sleep(PollMs);
        }
    }

    /// <summary>One change in the buttons held: the radial menu gets it if it's open, otherwise the chat panel.</summary>
    internal void OnButtons(uint prev, uint cur, bool probe)
    {
        var b = bindings;
        bool wasOpen = ChatOpen;

        if (radialMenu.IsOpen)
        {
            radialMenu.OnButtons(prev, cur, b.Radial, b.Back);
            return;
        }
        if (b.Radial.FiredBy(prev, cur))
        {
            radialMenu.Open();
            chat.Close();
            session.ChatClosing("Radial menu opened");
            if (wasOpen) Changed();
            return;
        }

        switch (chat.OnButtons(prev, cur, b))
        {
            case ChatAction.Dictate when probe:
                Log.Info($"  would dictate ({b.Dictate.Text})");
                break;
            case ChatAction.Dictate:
                session.Start(b.Dictate.Text);
                break;
            // Often a binding of its own in the game, so these are noted, not complained about.
            case ChatAction.DictateWhileClosed:
                Log.Info($"{b.Dictate.Text} with chat closed: nothing to dictate into.");
                break;
            case ChatAction.DictateInMenu:
                Log.Info($"{b.Dictate.Text} in a chat menu: nothing to dictate into.");
                break;
            case ChatAction.MenuOpened:
                session.ChatClosing("Chat menu opened", keepsText: true);
                break;
        }

        if (wasOpen != ChatOpen)
        {
            if (!chat.IsOpen) session.ChatClosing("Chat closed");
            Log.Info(ChatOpen ? "Chat open." : chat.IsOpen ? "In a chat menu." : "Chat closed.");
            Changed();
        }
    }

    /// <summary>The right stick while the radial menu is open: picking Chat there opens chat.</summary>
    internal void OnRightStick(float x, float y)
    {
        if (!radialMenu.OnRightStick(x, y)) return;
        chat.Open();
        Changed();
    }

    void Changed() => StateChanged?.Invoke();

    public async ValueTask DisposeAsync()
    {
        Stop();
        hotkey.Dispose();
        if (Interlocked.Exchange(ref transcriber, null) is { } t) await t.DisposeAsync().ConfigureAwait(false);
        configGate.Dispose();
    }
}

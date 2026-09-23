using System.Diagnostics;
using VoiceForever.Configuration;
using VoiceForever.Dictation;
using VoiceForever.Input;
using VoiceForever.Interop;
using VoiceForever.Logging;
using VoiceForever.Speech;

namespace VoiceForever;

/// <summary>
/// Owns the controller loop, the loaded speech model and the dictation session. The WinUI app
/// and the CLI are both thin shells around this.
/// </summary>
public sealed class Engine : IAsyncDisposable
{
    // One controller watcher per session: two would both type every message.
    static readonly Semaphore ControllerLock = new(1, 1, @"Local\VoiceForever.Controller");

    const int PollMs = 8;              // Windows' timer tick makes this ~15 ms in practice: still under a frame
    const int RescanMs = 1000;         // polling empty XInput slots is slow, so look for a new pad once a second
    const int MenuDepthTextBox = 0, MenuDepthMenu = 1, MenuDepthSubmenu = 2;

    readonly HotkeyListener hotkey = new();
    readonly RadialMenu radialMenu = new();
    readonly Session session;
    volatile Bindings bindings;
    volatile ChordRecorder? recorder;
    TaskCompletionSource<Chord>? recorded;
    volatile bool chatOpen;
    volatile int menuDepth; // MenuDepthTextBox, MenuDepthMenu or MenuDepthSubmenu
    volatile int controllerSlot = -1;
    volatile Thread? thread;
    Transcriber? transcriber;
    CancellationTokenSource? stopping;
    bool holdsLock;

    /// <exception cref="FormatException">A chord in the config doesn't parse.</exception>
    public Engine(Config config)
    {
        Config = config;
        bindings = Bindings.From(config);
        session = new Session(config, () => transcriber, (text, took, seconds) => Transcribed?.Invoke(text, took, seconds),
            phase => PhaseChanged?.Invoke(phase));
        hotkey.Pressed += () =>
        {
            if (IsRunning) session.Start(Config.KeyboardShortcut ?? "Shortcut", anyWindow: true);
        };
    }

    /// <summary>Raised on a background thread whenever any of the state properties change.</summary>
    public event Action? StateChanged;

    /// <summary>Raised on a background thread with each dictation: text, transcription time, audio seconds.</summary>
    public event Action<string, TimeSpan, double>? Transcribed;

    /// <summary>Raised on a background thread as a dictation starts listening, transcribes, and finishes.</summary>
    public event Action<DictationPhase>? PhaseChanged;

    public Config Config { get; }
    public bool IsRunning => thread is not null;
    public int ControllerSlot => controllerSlot;
    public bool IsLoadingModel => LoadingModel is not null;
    public string? LoadingModel { get; private set; }
    public string? LoadedModel { get; private set; }
    public string ModelStatus { get; private set; } = "No model loaded";

    /// <summary>The model last used, if its file was deleted since; set by <see cref="StartingModel"/>.</summary>
    public string? RemovedModel { get; private set; }

    /// <summary>The chat panel is open with its text box focused (not in one of its menus).</summary>
    public bool ChatOpen => chatOpen && menuDepth == MenuDepthTextBox;

    /// <summary>Why the last Start() refused, or null.</summary>
    public string? StartError { get; private set; }

    /// <summary>Why the keyboard shortcut couldn't be registered, or null.</summary>
    public string? KeyboardError { get; private set; }

    // ---- Bindings ---------------------------------------------------------------------------

    /// <summary>Swapped as a whole when the user rebinds, so the controller thread never sees half a change.</summary>
    sealed record Bindings(Chord OpenChat, Chord Dictate, Chord Send, Chord Back, Chord[] Menus, Chord Radial)
    {
        public static Bindings From(Config c) => new(
            Chord.Parse(c.OpenChatChord), Chord.Parse(c.DictateChord), Chord.Parse(c.SendChord), Chord.Parse(c.BackChord),
            [.. c.MenuChords.Select(Chord.Parse)], Chord.Parse(c.RadialMenuChord));
    }

    /// <summary>
    /// Records the next chord pressed on the controller instead of acting on it. Null if nothing
    /// was pressed before the timeout or cancellation. Needs the controller loop running.
    /// </summary>
    /// <exception cref="InvalidOperationException">The controller loop isn't running.</exception>
    public async Task<Chord?> CaptureChordAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!IsRunning) throw new InvalidOperationException("Set Voice Forever to Active first.");
        session.ChatClosing("Rebinding");
        chatOpen = false;
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

        if (which == BindingKind.OpenChat) Config.OpenChatChord = chord.Text;
        else Config.DictateChord = chord.Text;
        bindings = Bindings.From(Config);
        chatOpen = false;
        await Config.SaveAsync(ct).ConfigureAwait(false);
        Log.Info($"{(which == BindingKind.OpenChat ? "Open chat" : "Dictate")} is now {chord.Text}.");
        Changed();
        return null;
    }

    // ---- Keyboard shortcut ------------------------------------------------------------------

    /// <summary>Sets (or with null, turns off) the keyboard shortcut. Returns why it was refused, or null.</summary>
    public async Task<string?> SetKeyboardShortcutAsync(Shortcut? shortcut, CancellationToken ct = default)
    {
        if (shortcut is { } s && s.Modifiers == 0 && !s.IsFunctionKey)
            return $"{s} on its own would take that key from every program. Add Ctrl, Alt or Shift, or use an F key.";
        var previous = Config.KeyboardShortcut;
        hotkey.Unregister();
        Config.KeyboardShortcut = shortcut?.ToString();
        if (IsRunning) RegisterKeyboardShortcut();
        if (KeyboardError is { } error)
        {
            Config.KeyboardShortcut = previous;
            if (IsRunning) RegisterKeyboardShortcut();
            return error;
        }
        await Config.SaveAsync(ct).ConfigureAwait(false);
        if (shortcut is null) Log.Info("Keyboard shortcut is off.");
        Changed();
        return null;
    }

    /// <summary>While the app records a new shortcut, the old one mustn't fire.</summary>
    public void SuspendKeyboardShortcut() => hotkey.Unregister();

    public void ResumeKeyboardShortcut()
    {
        if (IsRunning) RegisterKeyboardShortcut();
    }

    void RegisterKeyboardShortcut()
    {
        KeyboardError = null;
        if (Config.KeyboardShortcut is not { } text) return;
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
        var installed = ModelCatalog.Installed();
        var saved = installed.FirstOrDefault(p => string.Equals(p, Config.ModelPath, StringComparison.OrdinalIgnoreCase));
        if (saved is not null) return saved;
        var fallback = ModelCatalog.All.Where(m => m.IsInstalled).Select(m => m.LocalPath).FirstOrDefault()
                       ?? (installed.Count > 0 ? installed[0] : null);
        if (Config.ModelPath.Length > 0)
        {
            RemovedModel = ModelCatalog.DisplayName(Config.ModelPath);
            Log.Warn(fallback is null
                ? $"{RemovedModel} is no longer in the models folder. Download a speech model to dictate."
                : $"{RemovedModel} is no longer in the models folder, so using {ModelCatalog.DisplayName(fallback)} instead.");
        }
        return fallback;
    }

    /// <summary>Loads a model and swaps it in; the old one is released once it's idle. Failures are logged, not thrown.</summary>
    public async Task LoadModelAsync(string path, CancellationToken ct = default)
    {
        var name = ModelCatalog.DisplayName(path);
        LoadingModel = path;
        ModelStatus = $"Loading {name}...";
        Changed();
        try
        {
            var started = Stopwatch.GetTimestamp();
            var loaded = await Transcriber.LoadAsync(Config, path, ct).ConfigureAwait(false);
            var old = Interlocked.Exchange(ref transcriber, loaded);
            LoadedModel = path;
            ModelStatus = $"{name} ready in {Stopwatch.GetElapsedTime(started).TotalSeconds:F1}s on {Transcriber.RuntimeInfo}";
            Log.Info(ModelStatus);
            if (Config.ModelPath != path)
            {
                Config.ModelPath = path;
                await Config.SaveAsync(ct).ConfigureAwait(false);
            }
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
        try
        {
            Cue.Start(Config);
            PhaseChanged?.Invoke(DictationPhase.Listening);
            await Task.Delay(Config.DelayMs, ct).ConfigureAwait(false);
            var audio = await Recorder.RecordUtteranceAsync(Config, finish, ct).ConfigureAwait(false);
            if (audio is null) return null;
            Cue.Heard(Config);
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
    /// Starts watching the controller; false if another Voice Forever already is. In probe mode
    /// it only logs presses, so it doesn't need the controller to itself.
    /// </summary>
    public bool Start(bool probe = false)
    {
        if (IsRunning) return true;
        if (!probe && !ControllerLock.WaitOne(0))
        {
            StartError = "Another Voice Forever window or CLI is already watching the controller. Close it first, or both would type.";
            Log.Warn(StartError);
            Changed();
            return false;
        }
        StartError = null;
        holdsLock = !probe;
        var cts = stopping = new CancellationTokenSource();
        // A dedicated thread, not a timer or a task: XInput has no events, so this is a blocking
        // poll for the app's lifetime, which would otherwise pin a thread-pool thread.
        var poller = new Thread(() => Poll(probe, cts.Token)) { IsBackground = true, Name = "Controller" };
        thread = poller;
        poller.Start();
        Log.Info(probe ? "Probe mode: logging presses only, nothing is recorded." : "Watching the controller.");
        if (!probe) RegisterKeyboardShortcut();
        Changed();
        return true;
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
        chatOpen = false;
        radialMenu.Reset();
        if (holdsLock) ControllerLock.Release();
        holdsLock = false;
        controllerSlot = -1;
        Log.Info("Stopped watching the controller.");
        Changed();
    }

    void Poll(bool probe, CancellationToken ct)
    {
        uint prev = 0;
        long nextScan = 0;
        while (!ct.IsCancellationRequested)
        {
            PadState? read = controllerSlot >= 0 ? Gamepad.Read(controllerSlot) : null;
            if (controllerSlot >= 0 && read is null)
            {
                Log.Warn($"Controller on slot {controllerSlot} disconnected.");
                controllerSlot = -1;
                Changed();
            }
            if (controllerSlot < 0 && Environment.TickCount64 >= nextScan)
            {
                nextScan = Environment.TickCount64 + RescanMs;
                controllerSlot = Gamepad.FindSlot(Config.ControllerSlot);
                if (controllerSlot >= 0)
                {
                    Log.Info($"Controller connected on slot {controllerSlot}.");
                    read = Gamepad.Read(controllerSlot);
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

    /// <summary>
    /// Follows WoW's gamepad chat panel from the same presses the game sees. Its menus reuse A and
    /// B: in the channel menu the first A opens a submenu (General, Custom, Language) and the next
    /// picks an item, landing back in the text box; B backs out one level. When unsure, it errs
    /// towards "not in the text box", which only costs a refused dictation.
    /// </summary>
    void OnButtons(uint prev, uint cur, bool probe)
    {
        var (openChat, dictate, send, back, menus, radial) = bindings;
        bool wasOpen = ChatOpen;

        if (radialMenu.IsOpen)
        {
            radialMenu.OnButtons(prev, cur, radial, back);
            return;
        }
        if (radial.FiredBy(prev, cur))
        {
            radialMenu.Open();
            chatOpen = false;
            menuDepth = MenuDepthTextBox;
            session.ChatClosing("Radial menu opened");
            if (wasOpen) Changed();
            return;
        }
        if (openChat.FiredBy(prev, cur))
        {
            chatOpen = true;
            menuDepth = MenuDepthTextBox;
        }
        else if (!chatOpen)
        {
            if (dictate.FiredBy(prev, cur))
            {
                Log.Warn($"{dictate.Text}: chat isn't open. Open it with {openChat.Text} first.");
                Cue.Error(Config);
            }
            return;
        }
        else if (dictate.FiredBy(prev, cur))
        {
            if (menuDepth > MenuDepthTextBox)
            {
                Log.Warn($"{dictate.Text}: close the chat menu first.");
                Cue.Error(Config);
            }
            else if (probe) Log.Info($"  would dictate ({dictate.Text})");
            else session.Start(dictate.Text);
        }
        else if (menuDepth == MenuDepthTextBox && menus.Any(m => m.FiredBy(prev, cur)))
        {
            menuDepth = MenuDepthMenu;
            session.ChatClosing("Chat menu opened", keepsText: true);
        }
        else if (send.FiredBy(prev, cur))
        {
            if (menuDepth == MenuDepthTextBox) chatOpen = false;
            else menuDepth = menuDepth == MenuDepthMenu ? MenuDepthSubmenu : MenuDepthTextBox;
        }
        else if (back.FiredBy(prev, cur))
        {
            if (menuDepth == MenuDepthTextBox) chatOpen = false;
            else menuDepth--;
        }

        if (wasOpen != ChatOpen)
        {
            if (!ChatOpen && !chatOpen) session.ChatClosing("Chat closed");
            Log.Info(ChatOpen ? "Chat open." : chatOpen ? "In a chat menu." : "Chat closed.");
            Changed();
        }
    }

    /// <summary>The right stick while the radial menu is open: picking Chat there opens chat.</summary>
    void OnRightStick(float x, float y)
    {
        if (!radialMenu.OnRightStick(x, y)) return;
        chatOpen = true;
        menuDepth = MenuDepthTextBox;
        Changed();
    }

    void Changed() => StateChanged?.Invoke();

    public async ValueTask DisposeAsync()
    {
        Stop();
        hotkey.Dispose();
        if (Interlocked.Exchange(ref transcriber, null) is { } t) await t.DisposeAsync().ConfigureAwait(false);
    }
}

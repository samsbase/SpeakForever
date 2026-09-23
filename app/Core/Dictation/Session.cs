using System.Diagnostics;
using SpeakForever.Configuration;
using SpeakForever.Interop;
using SpeakForever.Logging;
using SpeakForever.Speech;

namespace SpeakForever.Dictation;

/// <summary>
/// One dictation at a time: record until you pause (or press the trigger again) → transcribe →
/// type. From the controller it types only into the game's open chat box, and a chat box that
/// closes first discards it. From the keyboard shortcut it types into whatever has focus, like Win+H.
/// </summary>
/// <param name="settings">The current settings; each dictation reads them once, at its start.</param>
/// <param name="currentModel">The loaded model at the moment it's needed; it can change between dictations.</param>
/// <param name="transcribed">Each result: text, transcription time, seconds of audio.</param>
/// <param name="phase">Listening, then transcribing, then idle.</param>
/// <param name="tooLong">The words that didn't fit in the chat box, when some didn't.</param>
sealed class Session(Func<Config> settings, Func<Transcriber?> currentModel, Action<string, TimeSpan, double> transcribed,
                     Action<DictationPhase> phase, Action<string> tooLong)
{
    readonly Lock gate = new();
    CancellationTokenSource? active;
    CancellationTokenSource? finishing; // set while recording; triggering it ends the recording now
    int typed; // characters of ours in the chat box: the next dictation goes after them, and starting over deletes them
    string? restart; // started over while a dictation was running: start again, with this trigger, once it has stopped

    /// <param name="trigger">The button or shortcut, for the log.</param>
    /// <param name="anyWindow">Keyboard shortcut: type into whatever has focus, not only the game.</param>
    public void Start(string trigger, bool anyWindow = false) => Begin(trigger, anyWindow, startOver: false);

    /// <summary>
    /// The chat box's missing delete button: drops any dictation in flight, deletes what was
    /// dictated into the chat box, and listens again. Controller only.
    /// </summary>
    public void StartOver(string trigger)
    {
        Log.Info($"{trigger}: starting over.");
        lock (gate)
        {
            if (active is not null)
            {
                restart = trigger;
                active.Cancel();
                return; // RunAsync starts again once this one has stopped
            }
        }
        Begin(trigger, anyWindow: false, startOver: true);
    }

    /// <summary>Starts a dictation, or finishes the recording in progress. False if nothing started.</summary>
    bool Begin(string trigger, bool anyWindow, bool startOver)
    {
        var cfg = settings();
        lock (gate)
        {
            if (active is not null)
            {
                if (finishing is { } f)
                {
                    Log.Info($"{trigger}: done speaking.");
                    f.Cancel();
                }
                else Log.Info($"{trigger}: still transcribing.");
                return false;
            }
        }
        var fg = Native.Foreground();
        if (!anyWindow && !cfg.IsGame(fg.Process, fg.Path))
        {
            Log.Warn($"{trigger}: ignored, because WoW: Forever isn't the active window ({fg.Process} is).");
            return false;
        }
        if (currentModel() is null)
        {
            Log.Warn($"{trigger}: ignored, no speech model is loaded yet.");
            return false;
        }
        CancellationTokenSource cts, finish;
        lock (gate)
        {
            if (active is not null) return false;
            active = cts = new CancellationTokenSource();
            finishing = finish = new CancellationTokenSource();
        }
        // Off the caller's thread (the controller loop, or the hotkey listener): recording and
        // transcribing take seconds. RunAsync handles all of its own errors.
        _ = Task.Run(() => RunAsync(cfg, trigger, anyWindow, startOver, cts, finish));
        return true;
    }

    /// <summary>The chat box is closing or losing focus, so drop anything still in flight.</summary>
    public void ChatClosing(string why, bool keepsText = false)
    {
        lock (gate)
        {
            if (!keepsText) typed = 0;
            restart = null;
            if (active is null) return;
            active.Cancel();
        }
        Log.Info($"{why}, so the dictation was cancelled.");
    }

    /// <summary>Drops any dictation in flight, for when the controller loop stops.</summary>
    public void CancelAll() => ChatClosing("Paused");

    async Task RunAsync(Config cfg, string trigger, bool anyWindow, bool startOver, CancellationTokenSource cts, CancellationTokenSource finish)
    {
        var ct = cts.Token;
        try
        {
            if (startOver)
            {
                lock (gate)
                {
                    ct.ThrowIfCancellationRequested();
                    Erase();
                }
            }
            Cue.Start(cfg);
            phase(DictationPhase.Listening);
            await Task.Delay(cfg.DelayMs, ct).ConfigureAwait(false);
            Log.Info($"{trigger}: listening…");

            float[]? audio;
            try
            {
                audio = await Recorder.RecordUtteranceAsync(cfg, finish.Token, ct).ConfigureAwait(false);
            }
            finally
            {
                lock (gate)
                {
                    finishing = null;
                    finish.Dispose();
                }
            }
            if (audio is null)
            {
                Log.Info("Didn't hear any speech.");
                    return;
            }
            Cue.Heard(cfg);
            phase(DictationPhase.Transcribing);

            var transcriber = currentModel() ?? throw new InvalidOperationException("the speech model was unloaded");
            var started = Stopwatch.GetTimestamp();
            var text = await transcriber.TranscribeAsync(audio, ct).ConfigureAwait(false);
            var took = Stopwatch.GetElapsedTime(started);
            double seconds = audio.Length / (double)Recorder.SampleRate;
            Log.Info($"Transcribed {seconds:F1} s of speech in {took.TotalMilliseconds:F0} ms: \"{text}\"");
            transcribed(text, took, seconds);
            if (text.Length == 0) return;

            // Under the lock, so a chat box closing can't slip in between the check and the typing.
            string leftOut;
            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                leftOut = Type(cfg, text, anyWindow);
            }
            if (leftOut.Length > 0) tooLong(leftOut);
        }
        catch (OperationCanceledException)
        {
            // ChatClosing or StartOver already reported it.
        }
        catch (Exception e)
        {
            Log.Warn($"Dictation failed: {e.Message}");
        }
        finally
        {
            string? again;
            lock (gate)
            {
                if (active == cts) active = null;
                again = restart;
                restart = null;
            }
            cts.Dispose();
            // Starting over goes straight from one dictation to the next, without an idle moment in between.
            if (again is null || !Begin(again, anyWindow: false, startOver: true)) phase(DictationPhase.Idle);
        }
    }

    /// <summary>
    /// Types as much of the result as fits in the chat box, re-checking the game is still in front.
    /// Returns the words that didn't fit, or "". Called under the lock.
    /// </summary>
    string Type(Config cfg, string text, bool anyWindow)
    {
        int used = 0;
        if (!anyWindow)
        {
            var fg = Native.Foreground();
            if (!cfg.IsGame(fg.Process, fg.Path))
            {
                Log.Warn($"Didn't type it: WoW: Forever is no longer the active window ({fg.Process} is).");
                    return "";
            }
            if (typed > 0) text = " " + text;
            used = typed;
        }
        var (fits, leftOut) = ChatBox.Fit(text, used);
        if (leftOut.Length > 0)
            Log.Warn(fits.Length == 0
                ? $"The chat box is full (it holds {ChatBox.MaxLength} characters), so none of that was typed."
                : $"That's more than WoW's chat box holds ({ChatBox.MaxLength} characters). Left out: \"{leftOut}\"");
        if (fits.Length == 0) return leftOut;
        if (Native.TypeText(fits) is { } error)
        {
            Log.Warn(error);
            return "";
        }
        if (!anyWindow) typed += fits.Length;
        return leftOut;
    }

    /// <summary>Deletes what was dictated into the chat box, for starting over. Called under the lock.</summary>
    void Erase()
    {
        if (typed == 0) return;
        if (Native.Backspace(typed) is { } error)
        {
            Log.Warn(error);
            return;
        }
        Log.Info($"Deleted what was dictated into the chat box ({typed} characters).");
        typed = 0;
    }
}

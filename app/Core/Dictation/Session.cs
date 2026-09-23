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
sealed class Session(Func<Config> settings, Func<Transcriber?> currentModel, Action<string, TimeSpan, double> transcribed, Action<DictationPhase> phase)
{
    readonly Lock gate = new();
    CancellationTokenSource? active;
    CancellationTokenSource? finishing; // set while recording; triggering it ends the recording now
    bool chatHasOurText; // a second dictation into the same chat box gets a separating space

    /// <param name="trigger">The button or shortcut, for the log.</param>
    /// <param name="anyWindow">Keyboard shortcut: type into whatever has focus, not only the game.</param>
    public void Start(string trigger, bool anyWindow = false)
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
                return;
            }
        }
        var fg = Native.Foreground();
        if (!anyWindow && !cfg.IsGame(fg.Process, fg.Path))
        {
            Log.Warn($"{trigger}: ignored, because WoW: Forever isn't the active window ({fg.Process} is).");
            return;
        }
        if (currentModel() is null)
        {
            Log.Warn($"{trigger}: ignored, no speech model is loaded yet.");
            return;
        }
        CancellationTokenSource cts, finish;
        lock (gate)
        {
            if (active is not null) return;
            active = cts = new CancellationTokenSource();
            finishing = finish = new CancellationTokenSource();
        }
        // Off the caller's thread (the controller loop, or the hotkey listener): recording and
        // transcribing take seconds. RunAsync handles all of its own errors.
        _ = Task.Run(() => RunAsync(cfg, trigger, anyWindow, cts, finish));
    }

    /// <summary>The chat box is closing or losing focus, so drop anything still in flight.</summary>
    public void ChatClosing(string why, bool keepsText = false)
    {
        lock (gate)
        {
            if (!keepsText) chatHasOurText = false;
            if (active is null) return;
            active.Cancel();
        }
        Log.Info($"{why}, so the dictation was cancelled.");
    }

    /// <summary>Drops any dictation in flight, for when the controller loop stops.</summary>
    public void CancelAll() => ChatClosing("Paused");

    async Task RunAsync(Config cfg, string trigger, bool anyWindow, CancellationTokenSource cts, CancellationTokenSource finish)
    {
        var ct = cts.Token;
        try
        {
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
            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                Type(cfg, text, anyWindow);
            }
        }
        catch (OperationCanceledException)
        {
            // ChatClosing already reported it.
        }
        catch (Exception e)
        {
            Log.Warn($"Dictation failed: {e.Message}");
        }
        finally
        {
            lock (gate)
            {
                if (active == cts) active = null;
            }
            cts.Dispose();
            phase(DictationPhase.Idle);
        }
    }

    /// <summary>Types the result, re-checking the game is still in front. Called under the lock.</summary>
    void Type(Config cfg, string text, bool anyWindow)
    {
        if (!anyWindow)
        {
            var fg = Native.Foreground();
            if (!cfg.IsGame(fg.Process, fg.Path))
            {
                Log.Warn($"Didn't type it: WoW: Forever is no longer the active window ({fg.Process} is).");
                    return;
            }
            if (chatHasOurText) text = " " + text;
        }
        if (Native.TypeText(text) is { } error)
        {
            Log.Warn(error);
            return;
        }
        if (!anyWindow) chatHasOurText = true;
    }
}

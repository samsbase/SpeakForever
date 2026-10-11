using System.Diagnostics;
using SpeakForever.Configuration;
using SpeakForever.Interop;
using SpeakForever.Logging;
using SpeakForever.Speech;

namespace SpeakForever.Dictation;

/// <summary>
/// One dictation at a time: record until you pause (or press the trigger again) → transcribe →
/// copy to the clipboard, for you to paste into chat with Ctrl+V. Speak Forever never presses a
/// key in the game. Once it's listening, only the trigger or a pause ends it: closing chat or
/// pressing other buttons doesn't, so you can keep playing while you talk. The text is then ready
/// to paste until you paste it (Ctrl+V) or chat closes, and pressing the trigger again first
/// cancels it. From the controller it only starts with the game's chat box open; the keyboard
/// shortcut works any time, like Win+H.
/// </summary>
/// <param name="settings">The current settings; each dictation reads them once, at its start.</param>
/// <param name="currentModel">The loaded model at the moment it's needed; it can change between dictations.</param>
/// <param name="transcribed">Each result: text, transcription time, seconds of audio.</param>
/// <param name="phase">Listening, then transcribing, then ready to paste, then idle. Raised in order.</param>
/// <param name="tooLong">The words that didn't fit in the chat box, when some didn't; raised before Ready.</param>
sealed class Session(Func<Config> settings, Func<Transcriber?> currentModel, Action<string, TimeSpan, double> transcribed,
                     Action<DictationPhase> phase, Action<string> tooLong)
{
    readonly Lock gate = new();
    CancellationTokenSource? active;
    CancellationTokenSource? finishing; // set while recording; triggering it ends the recording now
    volatile bool ready; // our text is on the clipboard, waiting to be pasted
    uint copied; // the clipboard's sequence number when it was put there

    /// <summary>Text is on the clipboard, waiting to be pasted.</summary>
    public bool IsReady => ready;

    /// <summary>Listening or transcribing: the trigger finishes it wherever you are.</summary>
    public bool IsBusy => active is not null;

    /// <summary>Starts a dictation, finishes the recording in progress, or cancels text waiting to be pasted.</summary>
    /// <param name="trigger">The button or shortcut, for the log.</param>
    public void Start(string trigger)
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
            if (ready)
            {
                ready = false;
                Native.ClearClipboard(copied);
                phase(DictationPhase.Idle);
                Log.Info($"{trigger}: cancelled, and taken off the clipboard.");
                return;
            }
        }
        if (currentModel() is null)
        {
            Log.Warn($"{trigger}: ignored, no speech model is loaded yet.");
            return;
        }
        CancellationTokenSource cts, finish;
        lock (gate)
        {
            if (active is not null || ready) return;
            active = cts = new CancellationTokenSource();
            finishing = finish = new CancellationTokenSource();
        }
        // Off the caller's thread (the controller loop, or the hotkey listener): recording and
        // transcribing take seconds. RunAsync handles all of its own errors.
        _ = Task.Run(() => RunAsync(cfg, trigger, cts, finish));
    }

    /// <summary>
    /// The chat box is closing (sent, backed out of) or losing focus: text waiting to be pasted is
    /// done with. It stays on the clipboard. A dictation still listening or transcribing carries on,
    /// so you can fight while you talk and paste it when you open chat again.
    /// </summary>
    /// <param name="keepReady">A chat menu opened over the text box: text waiting to be pasted still is.</param>
    public void ChatClosing(string why, bool keepReady = false)
    {
        if (keepReady) return;
        bool done = false;
        lock (gate)
        {
            if (ready)
            {
                ready = false;
                phase(DictationPhase.Idle);
                done = true;
            }
        }
        if (done) Log.Info($"{why}: finished with the copied text.");
    }

    /// <summary>Drops any dictation in flight and any text waiting to be pasted, for when Speak Forever is paused.</summary>
    public void CancelAll()
    {
        bool cancelled;
        lock (gate)
        {
            cancelled = active is not null;
            active?.Cancel();
        }
        if (cancelled) Log.Info("Paused, so the dictation was cancelled.");
        ChatClosing("Paused");
    }

    async Task RunAsync(Config cfg, string trigger, CancellationTokenSource cts, CancellationTokenSource finish)
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

            var (fits, leftOut) = ChatBox.Fit(text);
            if (leftOut.Length > 0)
            {
                Log.Warn($"That's more than WoW's chat box holds ({ChatBox.MaxLength} characters), so only the start was copied. Left out: \"{leftOut}\"");
                tooLong(leftOut);
            }
            // Under the lock, so a chat box closing can't slip in between the check and the copy.
            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                if (Native.CopyText(fits, out copied) is { } error)
                {
                    Log.Warn(error);
                    return;
                }
                ready = true;
                phase(DictationPhase.Ready);
            }
            Log.Info("Copied. Paste it into chat with Ctrl+V.");
        }
        catch (OperationCanceledException)
        {
            // CancelAll already reported it.
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
                if (!ready) phase(DictationPhase.Idle);
            }
            cts.Dispose();
        }
    }
}

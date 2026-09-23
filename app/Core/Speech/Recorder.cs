using System.Runtime.InteropServices;
using NAudio.Wave;
using SpeakForever.Configuration;
using SpeakForever.Logging;

namespace SpeakForever.Speech;

/// <summary>
/// Records one utterance: until you pause, or until told to finish. Captures 16 kHz mono, which
/// is what Whisper consumes, so no resampling is needed.
/// </summary>
public static class Recorder
{
    public const int SampleRate = 16000;
    const int PaddingSamples = SampleRate * 300 / 1000;
    const int InitialSeconds = 10;

    /// <summary>
    /// The recorded speech, trimmed with a little padding, or null if nobody spoke. Triggering
    /// <paramref name="finish"/> ends it now and keeps what was said; <paramref name="ct"/> discards it.
    /// </summary>
    public static async Task<float[]?> RecordUtteranceAsync(Config cfg, CancellationToken finish, CancellationToken ct)
    {
        var samples = new List<float>(SampleRate * InitialSeconds);
        var endpointer = new Endpointer(cfg);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var wave = new WaveIn
        {
            DeviceNumber = cfg.MicDevice,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = Endpointer.FrameMs,
        };
        wave.DataAvailable += (_, e) =>
        {
            if (finished.Task.IsCompleted) return; // stopping; late buffers would move the end point
            lock (samples)
            {
                // 16-bit PCM straight from the buffer, no per-sample conversion calls.
                var pcm = MemoryMarshal.Cast<byte, short>(e.Buffer.AsSpan(0, e.BytesRecorded));
                int start = samples.Count;
                CollectionsMarshal.SetCount(samples, start + pcm.Length);
                var added = CollectionsMarshal.AsSpan(samples)[start..];
                for (int i = 0; i < pcm.Length; i++) added[i] = pcm[i] / 32768f;

                while (endpointer.Consumed + Endpointer.FrameSamples <= samples.Count)
                {
                    var frame = CollectionsMarshal.AsSpan(samples).Slice(endpointer.Consumed, Endpointer.FrameSamples);
                    if (endpointer.Feed(frame) is { } outcome) finished.TrySetResult(outcome);
                }
            }
        };
        wave.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) finished.TrySetException(e.Exception);
        };

        wave.StartRecording();
        bool heardSpeech;
        using (ct.Register(() => finished.TrySetCanceled(ct)))
        using (finish.Register(() =>
        {
            if (finished.Task.IsCompleted) return;
            endpointer.EndedBy = "when Finish was pressed";
            finished.TrySetResult(endpointer.HeardSpeech);
        }))
        {
            try { heardSpeech = await finished.Task.ConfigureAwait(false); }
            finally { wave.StopRecording(); }
        }
        // Enough to tell from the log why a recording ended when it did, or never did.
        Log.Info(heardSpeech
            ? $"Stopped listening {endpointer.EndedBy}. {endpointer.Levels}"
            : $"Didn't hear any speech. {endpointer.Levels}");
        if (!heardSpeech) return null;

        lock (samples)
        {
            int start = Math.Max(0, endpointer.SpeechStartSample - PaddingSamples);
            int end = Math.Min(samples.Count, endpointer.LastSpeechSample + PaddingSamples);
            return CollectionsMarshal.AsSpan(samples)[start..end].ToArray();
        }
    }
}

using SpeakForever.Configuration;

namespace SpeakForever.Speech;

/// <summary>
/// Energy-based end-of-speech detection over 30 ms frames. The room's noise level is the quietest
/// 150 ms seen so far, not a sample taken at the start: people often talk straight after the beep,
/// and the gaps between their phrases still show the real background.
/// </summary>
public sealed class Endpointer(Config cfg)
{
    public const int FrameMs = 30;
    public const int FrameSamples = Recorder.SampleRate * FrameMs / 1000;

    const int SpeechStartFrames = 3; // 90 ms of loud frames counts as speech
    const int SmoothFrames = 5;      // 150 ms averages for the room estimate, so one dropout can't drag it down
    const double FloorDb = -55;      // stops a dead-silent mic from treating hiss as speech
    const double Epsilon = 1e-10;    // keeps log10 finite on digital silence

    readonly List<double> frameDb = [];
    readonly List<double> framePower = [];
    double noiseDb = double.MaxValue, loudestDb = double.MinValue;
    int speechStartFrame = -1, lastLoudFrame = -1;

    public int Consumed { get; private set; }
    public int SpeechStartSample => speechStartFrame < 0 ? -1 : speechStartFrame * FrameSamples;
    public int LastSpeechSample => (lastLoudFrame + 1) * FrameSamples;
    public bool HeardSpeech => speechStartFrame >= 0;
    public string EndedBy { get; set; } = "?";
    public string Levels => $"Levels: background {noiseDb:F0} dB, speech above {Threshold:F0} dB, loudest {loudestDb:F0} dB.";

    double Threshold => Math.Max(noiseDb + cfg.SpeechThresholdDb, FloorDb);

    /// <summary>True when speech has ended, false when none came, null to keep recording.</summary>
    public bool? Feed(ReadOnlySpan<float> frame)
    {
        double sum = 0;
        foreach (var s in frame) sum += s * s;
        double power = sum / frame.Length;
        framePower.Add(power);
        frameDb.Add(10 * Math.Log10(power + Epsilon));
        Consumed += frame.Length;
        loudestDb = Math.Max(loudestDb, frameDb[^1]);

        int n = frameDb.Count;
        if (n < SmoothFrames) return null;
        double recent = 0;
        for (int i = n - SmoothFrames; i < n; i++) recent += framePower[i];
        noiseDb = Math.Min(noiseDb, 10 * Math.Log10(recent / SmoothFrames + Epsilon));

        // The room estimate only ever drops, so re-judge from the start each time: speech that
        // began before the estimate settled still counts, from its first word. Only until speech
        // is found, so at most the no-speech timeout's worth of frames (200) is rescanned.
        double threshold = Threshold;
        if (speechStartFrame < 0)
        {
            for (int i = 0, run = 0; i < n; i++)
            {
                run = frameDb[i] > threshold ? run + 1 : 0;
                if (run == SpeechStartFrames)
                {
                    speechStartFrame = i - SpeechStartFrames + 1;
                    break;
                }
            }
            if (speechStartFrame < 0)
                return n * FrameMs >= cfg.NoSpeechTimeoutSeconds * 1000 ? false : null;
        }
        for (int i = n - 1; i > lastLoudFrame; i--)
        {
            if (frameDb[i] > threshold)
            {
                lastLoudFrame = i;
                break;
            }
        }

        int quietMs = (n - 1 - lastLoudFrame) * FrameMs;
        if (quietMs >= cfg.SilenceMs) EndedBy = $"after a {cfg.SilenceMs / 1000.0:F1} s pause";
        else if (n * FrameMs >= cfg.MaxSeconds * 1000) EndedBy = $"at the {cfg.MaxSeconds} s limit";
        else return null;
        return true;
    }
}

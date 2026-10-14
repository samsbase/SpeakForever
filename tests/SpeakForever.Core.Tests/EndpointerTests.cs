using SpeakForever.Configuration;
using SpeakForever.Speech;

namespace SpeakForever.Core.Tests;

/// <summary>End-of-speech detection, fed synthetic audio 30 ms at a time.</summary>
public sealed class EndpointerTests
{
    const int Rate = Recorder.SampleRate, Frame = Endpointer.FrameSamples;

    readonly Random rng = new(1);
    double noiseLevel = 0.002;

    // Room noise whose loudness wanders from frame to frame (fans, game audio), not steady hiss.
    float Noise(int i)
    {
        if (i % Frame == 0) noiseLevel = 0.002 * Math.Pow(10, (rng.NextDouble() * 8 - 4) / 20); // ±4 dB per frame
        return (float)((rng.NextDouble() - 0.5) * 2 * noiseLevel);
    }

    float[] Tone(double seconds, double amplitude)
    {
        var samples = new float[(int)(seconds * Rate)];
        for (int i = 0; i < samples.Length; i++) samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * 200 * i / Rate)) + Noise(i);
        return samples;
    }

    float[] Quiet(double seconds) => Tone(seconds, 0);

    sealed record Outcome(bool? Result, double At, string EndedBy, string Levels, double SpeechStart);

    Outcome Run(Config cfg, params float[][] parts)
    {
        var audio = parts.SelectMany(p => p).Concat(Quiet(10)).ToArray();
        var e = new Endpointer(cfg);
        for (int i = 0; i + Frame <= audio.Length; i += Frame)
            if (e.Feed(audio.AsSpan(i, Frame)) is { } done)
                return new(done, (i + Frame) / (double)Rate, e.EndedBy, e.Levels, e.SpeechStartSample / (double)Rate);
        return new(null, audio.Length / (double)Rate, e.EndedBy, e.Levels, e.SpeechStartSample / (double)Rate);
    }

    [Fact]
    public void EndsAfterThePauseFollowingSpeech()
    {
        var r = Run(new Config(), Quiet(0.15), Tone(3, 0.1));
        Assert.True(r.Result);
        Assert.InRange(r.At - 3.15, 1.35, 1.65); // the 1.5 s default pause
    }

    [Fact]
    public void RidesOutAPauseShorterThanTheSetting()
    {
        var r = Run(new Config(), Quiet(0.15), Tone(2, 0.1), Quiet(1.2), Tone(2, 0.1));
        Assert.True(r.Result);
        Assert.True(r.At > 5.35, $"ended at {r.At:F2}s, mid-sentence");
    }

    [Fact]
    public void AShorterSettingEndsAtTheFirstPause()
    {
        var r = Run(new Config { SilenceMs = 900 }, Quiet(0.15), Tone(2, 0.1), Quiet(1.2), Tone(2, 0.1));
        Assert.True(r.At < 4);
    }

    [Fact]
    public void NoiseAloneIsNeverSpeech()
    {
        var r = Run(new Config(), Quiet(8));
        Assert.False(r.Result);
    }

    [Fact]
    public void LongTalkWithShortBreathsIsNotCutOff()
    {
        var parts = new List<float[]> { Quiet(0.15) };
        for (int i = 0; i < 20; i++)
        {
            parts.Add(Tone(4, 0.1));
            parts.Add(Quiet(0.6));
        }
        var r = Run(new Config(), [.. parts]);
        Assert.True(r.At > 90, $"ended at {r.At:F1}s by {r.EndedBy}");
    }

    [Fact]
    public void TalkingFromTheFirstInstantIsHeardFromTheStart()
    {
        var r = Run(new Config(), Tone(1.5, 0.1), Quiet(0.3), Tone(1.5, 0.1));
        Assert.True(r.Result);
        Assert.True(r.SpeechStart < 0.05);
        Assert.StartsWith("Levels: background -6", r.Levels, StringComparison.Ordinal); // the real background, not the voice
    }

    [Fact]
    public void LoudnessIsLowInTheRoomAndHighWhileTalking()
    {
        var e = new Endpointer(new Config());
        double Last(float[] audio)
        {
            for (int i = 0; i + Frame <= audio.Length; i += Frame) e.Feed(audio.AsSpan(i, Frame));
            return e.Loudness;
        }
        Assert.InRange(Last(Quiet(0.5)), 0, 0.3);
        Assert.InRange(Last(Tone(0.3, 0.1)), 0.9, 1);
    }

    /// <summary>As Run, with a voice detector that hears a voice between these times, checking every 150 ms like Recorder.</summary>
    Outcome RunWithVoice(double voiceFrom, double voiceTo, params float[][] parts)
    {
        var audio = parts.SelectMany(p => p).Concat(Quiet(10)).ToArray();
        var e = new Endpointer(new Config());
        for (int i = 0; i + Frame <= audio.Length; i += Frame)
        {
            if (i / Frame % 5 == 0) e.Voice(i >= voiceFrom * Rate ? (int)Math.Min(i, voiceTo * Rate) : -1);
            if (e.Feed(audio.AsSpan(i, Frame)) is { } done) return new(done, (i + Frame) / (double)Rate, e.EndedBy, e.Levels, e.SpeechStartSample / (double)Rate);
        }
        return new(null, audio.Length / (double)Rate, e.EndedBy, e.Levels, e.SpeechStartSample / (double)Rate);
    }

    [Fact]
    public void TypingAfterSpeechDoesNotKeepItListening()
    {
        // 3 s of talking, then 6 s of typing just as loud: the pause is timed from the voice.
        var r = RunWithVoice(0.2, 3.2, Quiet(0.15), Tone(3, 0.1), Tone(6, 0.05));
        Assert.True(r.Result);
        Assert.InRange(r.At - 3.2, 1.35, 1.8);
    }

    [Fact]
    public void LoudNoiseWithNoVoiceIsNoSpeech()
    {
        var r = RunWithVoice(double.MaxValue, 0, Quiet(0.15), Tone(10, 0.05));
        Assert.False(r.Result);
        Assert.InRange(r.At, 5.9, 6.2); // the no-speech timeout, not the 120 s limit
    }

    [Fact]
    public void AClickAsTheMicOpensIsIgnored()
    {
        var r = Run(new Config(), Tone(0.06, 0.3), Quiet(0.4), Tone(2, 0.1));
        Assert.True(r.Result);
        Assert.True(r.SpeechStart > 0.4);
    }
}

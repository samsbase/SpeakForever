using SpeakForever.Logging;
using Whisper.net;

namespace SpeakForever.Speech;

/// <summary>
/// Silero VAD, run by whisper.cpp: tells a voice from typing, clicks and game sounds, which
/// loudness alone can't. Measured on a microphone recording of speech then typing, it found the
/// speech and none of the typing, which kept the loudness rule listening indefinitely.
/// </summary>
public sealed class VoiceDetector : IDisposable
{
    // ggml-org/whisper-vad, the whisper.cpp project's own conversion; shipped beside the app (864 KB).
    const string ModelFile = "ggml-silero-v6.2.0.bin";

    static readonly Lazy<WhisperVadFactory?> Factory = new(Load);

    readonly WhisperVadProcessor processor;

    VoiceDetector(WhisperVadProcessor processor) => this.processor = processor;

    /// <summary>A detector for one recording, or null if the model can't load: loudness alone then decides.</summary>
    public static VoiceDetector? TryCreate() => Factory.Value is { } factory ? new(factory.CreateBuilder().Build()) : null;

    /// <summary>Where the last voice in the audio ends, in samples from its start, or -1 if there's none.</summary>
    public int LastVoiceEnd(ReadOnlySpan<float> audio)
    {
        var segments = processor.DetectSpeech(audio);
        return segments.Count == 0 ? -1 : Math.Min(audio.Length, (int)(segments[^1].End.TotalSeconds * Recorder.SampleRate));
    }

    static WhisperVadFactory? Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ModelFile);
        try
        {
            // On the processor: a few milliseconds a check, and it stays out of the game's video memory.
            return WhisperVadFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = false });
        }
        catch (Exception e) // native load failures surface as several exception types
        {
            Log.Warn($"The voice detector didn't load ({e.Message}), so typing and other noise can keep Speak Forever listening.");
            return null;
        }
    }

    public void Dispose() => processor.Dispose();
}

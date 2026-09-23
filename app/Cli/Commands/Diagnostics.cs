using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SpeakForever.Configuration;
using SpeakForever.Interop;
using SpeakForever.Logging;
using SpeakForever.Speech;

namespace SpeakForever.Cli.Commands;

/// <summary>Setup checks for the CLI.</summary>
static class Diagnostics
{
    /// <summary>One dictation without the game: record until you pause, then print the text.</summary>
    public static async Task<int> TestMicAsync(Engine engine, CancellationToken ct)
    {
        for (int i = 0; i < WaveIn.DeviceCount; i++)
            Log.Info($"  mic {i}: {WaveIn.GetCapabilities(i).ProductName}{(i == engine.Config.MicDevice ? "  <- configured" : "")}");
        if (engine.Config.MicDevice == -1) Log.Info("  using the Windows default microphone (MicDevice -1)");

        Log.Info("Say something after the beep; stop talking to finish.");
        var result = await engine.TestMicAsync(ct: ct);
        if (result is not var (text, took, seconds))
        {
            Log.Warn($"Heard no speech in {engine.Config.NoSpeechTimeoutSeconds}s. Check the microphone, or lower SpeechThresholdDb.");
            return 1;
        }
        Log.Info($"Heard {seconds:F1}s, transcribed in {took.TotalMilliseconds:F0} ms:");
        Log.Info($"  \"{text}\"");
        return 0;
    }

    /// <summary>Does typed text reach WoW's chat box? Open chat in game within the countdown.</summary>
    public static async Task<int> TestTypeAsync(Config cfg, string text, CancellationToken ct)
    {
        const int Seconds = 5;
        Log.Info($"Typing \"{text}\" in {Seconds}s. Switch to WoW and open the chat box now.");
        for (int i = Seconds; i > 0; i--)
        {
            Log.Info($"{i}...");
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        var fg = Native.Foreground();
        if (!cfg.IsGame(fg.Process))
        {
            Log.Warn($"Not typing: foreground is '{fg.Process}', not the game ({string.Join(", ", cfg.ProcessNames)}).");
            return 1;
        }
        if (Native.TypeText(text) is { } error)
        {
            Log.Warn(error);
            return 1;
        }
        Log.Info("Typed. Check the chat box, then press B to discard it.");
        return 0;
    }

    /// <summary>A .wav file as the 16 kHz mono samples Whisper takes, streamed through NAudio's converters.</summary>
    public static float[] ReadWav(string path)
    {
        using var reader = new WaveFileReader(path);
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
        if (source.WaveFormat.SampleRate != Recorder.SampleRate)
            source = new WdlResamplingSampleProvider(source, Recorder.SampleRate);
        // Sized from the file's length up front, so the list doesn't regrow as it fills.
        double seconds = reader.TotalTime.TotalSeconds;
        var samples = new List<float>((int)Math.Ceiling(seconds * Recorder.SampleRate));
        var buffer = new float[Recorder.SampleRate];
        int read;
        while ((read = source.Read(buffer)) > 0)
            samples.AddRange(buffer.AsSpan(0, read));
        return [.. samples];
    }

    /// <summary>Transcribes a .wav file, for checking accuracy and speed without a microphone.</summary>
    public static async Task<int> TranscribeFileAsync(Engine engine, string path, CancellationToken ct)
    {
        var samples = ReadWav(path);
        var (text, took) = await engine.TranscribeAsync(samples, ct);
        Log.Info($"{Path.GetFileName(path)}: {samples.Length / (double)Recorder.SampleRate:F1}s transcribed in {took.TotalMilliseconds:F0} ms:");
        Log.Info($"  \"{text}\"");
        return 0;
    }
}

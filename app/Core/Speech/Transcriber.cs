using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SpeakForever.Configuration;
using SpeakForever.Logging;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace SpeakForever.Speech;

/// <summary>
/// Local Whisper via whisper.cpp, with OpenAI's reference fallback rules: temperature 0 with 0.2
/// fallback steps, and its compression, log-prob and no-speech thresholds. The beam width is a
/// setting; greedy (1) is the default, since it matched 5-beam accuracy on turbo in the benchmark.
/// </summary>
public sealed partial class Transcriber : IAsyncDisposable
{
    const int MaxChatLength = 255; // WoW's chat edit box limit

    static string? gpuName;
    static readonly IDisposable DeviceLogger = LogProvider.AddLogger((_, message) =>
    {
        // whisper.cpp names the GPU as it loads: "ggml_vulkan: 0 = NVIDIA GeForce RTX 5070 Ti (NVIDIA) | ..."
        if (message is not null && DeviceLine().Match(message) is { Success: true } m) gpuName = m.Groups[1].Value;
    });

    readonly WhisperFactory factory;
    readonly WhisperProcessor processor;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly NameCorrector? names;
    bool disposed;

    Transcriber(Config cfg, string modelPath)
    {
        GC.KeepAlive(DeviceLogger); // registers the logger before the first model loads
        // Vulkan works on any recent GPU through its driver; CPU is the fallback.
        RuntimeOptions.RuntimeLibraryOrder = cfg.UseGpu ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu] : [RuntimeLibrary.Cpu];
        // Flash attention computes the same result without building Whisper's big attention matrices.
        // That matters most when a game has filled video memory and the model runs from system RAM:
        // measured with WoW running, it cut an 8 s clip from 4.6 s to 1.2 s, word-for-word identical.
        factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = cfg.UseGpu, UseFlashAttention = true });

        var builder = factory.CreateBuilder();
        builder = cfg.BeamSize > 1
            ? builder.WithBeamSearchSamplingStrategy(b => b.WithBeamSize(cfg.BeamSize))
            : builder.WithGreedySamplingStrategy(_ => { });
        builder = builder
            .WithTemperature(0f)
            .WithTemperatureInc(0.2f)
            .WithEntropyThreshold(2.4f)
            .WithLogProbThreshold(-1f)
            .WithNoSpeechThreshold(0.6f)
            .WithNoContext();
        if (!string.IsNullOrWhiteSpace(cfg.Prompt)) builder = builder.WithPrompt(cfg.Prompt);
        processor = (cfg.Language == "auto" ? builder.WithLanguageDetection() : builder.WithLanguage(cfg.Language)).Build();
        names = cfg.CorrectNames ? NameCorrector.Shared : null; // built here, off the UI thread, not on the first message
    }

    /// <summary>
    /// Loads a model on the thread pool (reading a model file takes seconds) and runs it once: the
    /// first GPU run compiles shaders, better at startup than on your first message.
    /// </summary>
    public static async Task<Transcriber> LoadAsync(Config cfg, string modelPath, CancellationToken ct = default)
    {
        var transcriber = await Task.Run(() => new Transcriber(cfg, modelPath), ct).ConfigureAwait(false);
        try
        {
            await transcriber.TranscribeAsync(new float[Recorder.SampleRate], ct).ConfigureAwait(false);
            return transcriber;
        }
        catch
        {
            await transcriber.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>"NVIDIA GeForce RTX 5070 Ti (Vulkan)", or "the processor".</summary>
    public static string RuntimeInfo
    {
        get
        {
            var library = RuntimeOptions.LoadedLibrary?.ToString() ?? "?";
            return library == "Cpu" ? "the processor" : gpuName is not null ? $"{gpuName} ({library})" : library;
        }
    }

    /// <exception cref="ObjectDisposedException">The model was switched out while this waited its turn.</exception>
    public async Task<string> TranscribeAsync(float[] audio, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(audio, ct).ConfigureAwait(false))
                text.Append(segment.Text);
            return Clean(text.ToString(), names);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Transcribes and times it, for the diagnostics.</summary>
    public async Task<(string Text, TimeSpan Took)> TimedAsync(float[] audio, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var text = await TranscribeAsync(audio, ct).ConfigureAwait(false);
        return (text, Stopwatch.GetElapsedTime(started));
    }

    internal static string Clean(string raw, NameCorrector? names = null)
    {
        // Whisper marks non-speech as [BLANK_AUDIO], (music), *laughs* and the like.
        var text = NonSpeech().Replace(raw, " ");
        text = Whitespace().Replace(text, " ").Trim();
        // "|" starts an escape sequence in WoW chat.
        text = text.Replace('|', '/');
        if (names is not null)
        {
            (text, var changes) = names.Correct(text);
            foreach (var (heard, name) in changes) Log.Info($"Heard \"{heard}\" as {name}.");
        }
        if (text.Length <= MaxChatLength) return text;

        int cut = text.LastIndexOf(' ', MaxChatLength);
        var kept = text[..(cut > 0 ? cut : MaxChatLength)];
        Log.Warn($"That was {text.Length} characters, but WoW's chat box holds {MaxChatLength}. Left out: \"{text[kept.Length..].Trim()}\"");
        return kept;
    }

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
    private static partial Regex NonSpeech();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"ggml_vulkan: \d+ = (.+?) \(")]
    private static partial Regex DeviceLine();

    /// <summary>Waits for any transcription in progress, so a model switch can't pull the model out from under it.</summary>
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            await processor.DisposeAsync().ConfigureAwait(false);
            factory.Dispose();
        }
        finally
        {
            // Released, not disposed: anything still queued behind us gets ObjectDisposedException
            // instead of waiting forever.
            gate.Release();
        }
    }
}

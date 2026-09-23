using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpeakForever.Configuration;
using SpeakForever.Logging;
using SpeakForever.Speech;

namespace SpeakForever.Cli.Commands;

/// <summary>
/// --benchmark: memory, speed and accuracy for every installed model at both decoding widths.
/// Each combination runs in its own process so memory readings don't mix. The test set is
/// %LOCALAPPDATA%\SpeakForever\benchmark\*.wav, each with a .txt of what was said; each clip is
/// also run with white noise mixed in, since clean audio hides differences between models.
/// </summary>
static partial class Benchmark
{
    const double NoiseSnrDb = 10;
    const string ResultPrefix = "RESULT ";
    const double BytesPerMB = 1024 * 1024;
    static readonly int[] BeamWidths = [5, 1];
    static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(100);

    static readonly string[] NumberWords =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
         "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty"];

    /// <param name="filter">Only models whose file name contains this, e.g. "turbo-q".</param>
    public static async Task<int> RunAllAsync(string? filter, CancellationToken ct)
    {
        int clips = Directory.Exists(AppPaths.Benchmark) ? Directory.EnumerateFiles(AppPaths.Benchmark, "*.wav").Count() : 0;
        if (clips == 0)
        {
            Log.Warn($@"No clips in {AppPaths.Benchmark}. Run: powershell.exe -File tools\make-benchmark-clips.ps1");
            return 1;
        }
        bool wow = Process.GetProcessesByName("WowB").Length > 0;
        Log.Info($"Benchmarking on {clips} clips, clean and with noise at {NoiseSnrDb} dB SNR. WoW running: {(wow ? "yes" : "no")}.");

        var results = new List<BenchmarkResult>();
        foreach (var model in ModelCatalog.Installed().Where(m => filter is null || Path.GetFileName(m).Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (int beam in BeamWidths)
            {
                if (await RunChildAsync(model, beam, ct) is { } r)
                {
                    results.Add(r);
                    Log.Info($"{r.Model}, beam {r.Beam}: done.");
                }
            }
        }
        Report(results);
        return 0;
    }

    /// <summary>Runs one combination in a child process and reads back its RESULT line.</summary>
    static async Task<BenchmarkResult?> RunChildAsync(string model, int beam, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!, ["--benchmark-one", model, beam.ToString(CultureInfo.InvariantCulture)])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var child = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start the benchmark process.");
        string? json = null;
        while (await child.StandardOutput.ReadLineAsync(ct) is { } line)
            if (line.StartsWith(ResultPrefix, StringComparison.Ordinal)) json = line[ResultPrefix.Length..];
        await child.WaitForExitAsync(ct);
        if (json is null)
        {
            Log.Warn($"{ModelCatalog.DisplayName(model)}, beam {beam}: run failed (exit code {child.ExitCode}).");
            return null;
        }
        return JsonSerializer.Deserialize<BenchmarkResult>(json);
    }

    static void Report(List<BenchmarkResult> results)
    {
        Log.Info("");
        Log.Info("Memory is in MB. GPU memory sits in video memory (VRAM) when there's room, otherwise Windows");
        Log.Info("spills it to system RAM (Spilled). 'Idle' is what's held between dictations, while you play.");
        Log.Info("");
        Log.Info($"{"Model",-22} {"Beam",4} {"Load",6} {"Idle RAM",9} {"Idle VRAM",10} {"Idle spill",11} {"Peak GPU",9} {"WER clean",10} {"WER noisy",10} {"ms/clip",8}");
        foreach (var r in results)
        {
            Log.Info($"{r.Model,-22} {r.Beam,4} {r.LoadSeconds,5:F1}s {r.Idle.PrivateMB,9:F0} {r.Idle.VramMB,10:F0} {r.Idle.SpilledMB,11:F0} " +
                     $"{r.Peak.VramMB + r.Peak.SpilledMB,9:F0} {Wer(r.CleanErrors, r.Words),10} {Wer(r.NoisyErrors, r.Words),10} {r.MsPerClip,8:F0}");
        }
        if (results.Count > 0)
            Log.Info($"Before loading any model the process holds {results.Min(r => r.Baseline.PrivateMB):F0} MB of RAM.");

        foreach (var r in results.Where(r => r.Mistakes.Count > 0))
        {
            Log.Info("");
            Log.Info($"{r.Model}, beam {r.Beam}, mistakes:");
            foreach (var m in r.Mistakes) Log.Info("  " + m);
        }
    }

    static string Wer(int errors, int words) => $"{100.0 * errors / words:F1}%";

    /// <summary>One model at one beam width, in its own process. Prints a RESULT line for RunAllAsync.</summary>
    public static async Task<int> RunOneAsync(Config cfg, string model, int beam, CancellationToken ct)
    {
        cfg = cfg with { BeamSize = beam };
        var clips = Directory.EnumerateFiles(AppPaths.Benchmark, "*.wav").Order()
            .Select(wav => (Name: Path.GetFileNameWithoutExtension(wav), Audio: Diagnostics.ReadWav(wav),
                            Reference: File.ReadAllText(Path.ChangeExtension(wav, ".txt")).Trim()))
            .ToList();

        var baseline = Sample();
        var started = Stopwatch.GetTimestamp();
        var transcriber = await Transcriber.LoadAsync(cfg, model, ct);
        await using (transcriber)
        {
            double load = Stopwatch.GetElapsedTime(started).TotalSeconds;
            await Task.Delay(TimeSpan.FromSeconds(1), ct); // let allocations settle before reading the idle footprint
            var idle = Sample();

            // Memory peaks mid-transcription, so it's sampled alongside rather than after.
            var peak = idle;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var sampler = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(SampleInterval);
                try
                {
                    while (await timer.WaitForNextTickAsync(stop.Token))
                        peak = MemorySample.Max(peak, Sample());
                }
                catch (OperationCanceledException)
                {
                }
            }, CancellationToken.None);

            int words = 0, clean = 0, noisy = 0;
            double totalMs = 0;
            var mistakes = new List<string>();
            foreach (var (name, audio, reference) in clips)
            {
                var expected = Words(reference);
                words += expected.Length;
                foreach (bool withNoise in (bool[])[false, true])
                {
                    var (text, took) = await transcriber.TimedAsync(withNoise ? AddNoise(audio, Seed(name)) : audio, ct);
                    totalMs += took.TotalMilliseconds;
                    int errors = WordErrors(expected, Words(text));
                    if (withNoise) noisy += errors; else clean += errors;
                    if (errors > 0) mistakes.Add($"{name}{(withNoise ? " +noise" : "")}: \"{text}\"");
                }
            }

            await stop.CancelAsync();
            await sampler;
            var shortName = Path.GetFileNameWithoutExtension(model).Replace("ggml-", "", StringComparison.Ordinal);
            Console.WriteLine(ResultPrefix + JsonSerializer.Serialize(new BenchmarkResult(
                shortName, beam, load, baseline, idle, peak, words, clean, noisy, totalMs / (clips.Count * 2), mistakes)));
        }
        return 0;
    }

    static MemorySample Sample()
    {
        using var self = Process.GetCurrentProcess();
        double vram = 0, spilled = 0;
        var prefix = $"pid_{Environment.ProcessId}_";
        foreach (var instance in new PerformanceCounterCategory("GPU Process Memory").GetInstanceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            try
            {
                using var dedicated = new PerformanceCounter("GPU Process Memory", "Dedicated Usage", instance, readOnly: true);
                using var shared = new PerformanceCounter("GPU Process Memory", "Shared Usage", instance, readOnly: true);
                vram += dedicated.RawValue / BytesPerMB;
                spilled += shared.RawValue / BytesPerMB;
            }
            catch (InvalidOperationException)
            {
                // The instance went away between listing and reading it.
            }
        }
        return new MemorySample(self.PrivateMemorySize64 / BytesPerMB, vram, spilled);
    }

    /// <summary>A stable seed per clip, so every model hears exactly the same noise.</summary>
    static int Seed(string name) => name.Aggregate(17, (h, c) => h * 31 + c);

    static float[] AddNoise(float[] audio, int seed)
    {
        double sumSquares = 0;
        foreach (var x in audio) sumSquares += (double)x * x;
        double rms = Math.Sqrt(sumSquares / audio.Length);
        double amplitude = rms / Math.Pow(10, NoiseSnrDb / 20) * Math.Sqrt(3); // uniform noise has RMS = amplitude / sqrt(3)
        var rng = new Random(seed);
        var noisy = new float[audio.Length];
        for (int i = 0; i < audio.Length; i++) noisy[i] = (float)(audio[i] + (rng.NextDouble() * 2 - 1) * amplitude);
        return noisy;
    }

    /// <summary>Lower case, no punctuation, and "5" and "five" count as the same word.</summary>
    static string[] Words(string text)
    {
        var cleaned = NotWordChars().Replace(text.ToLowerInvariant().Replace('-', ' '), " ");
        return [.. cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Array.IndexOf(NumberWords, w) is var i and >= 0 ? i.ToString(CultureInfo.InvariantCulture) : w)];
    }

    /// <summary>Word-level edit distance: substitutions, insertions and deletions.</summary>
    static int WordErrors(string[] reference, string[] hypothesis)
    {
        var row = new int[hypothesis.Length + 1];
        for (int j = 0; j < row.Length; j++) row[j] = j;
        for (int i = 1; i <= reference.Length; i++)
        {
            int diagonal = row[0];
            row[0] = i;
            for (int j = 1; j <= hypothesis.Length; j++)
            {
                int above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (reference[i - 1] == hypothesis[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }
        return row[hypothesis.Length];
    }

    [GeneratedRegex(@"[^a-z0-9' ]")]
    private static partial Regex NotWordChars();
}

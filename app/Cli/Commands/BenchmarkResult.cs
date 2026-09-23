namespace SpeakForever.Cli.Commands;

/// <summary>One model at one beam width; passed from the child process to the parent as JSON.</summary>
sealed record BenchmarkResult(
    string Model, int Beam, double LoadSeconds, MemorySample Baseline, MemorySample Idle, MemorySample Peak,
    int Words, int CleanErrors, int NoisyErrors, double MsPerClip, List<string> Mistakes);

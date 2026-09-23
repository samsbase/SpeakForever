namespace VoiceForever.Cli.Commands;

/// <summary>A process's memory at one moment, in MB.</summary>
/// <param name="PrivateMB">System RAM the process holds.</param>
/// <param name="VramMB">GPU memory in video memory.</param>
/// <param name="SpilledMB">GPU memory Windows has moved to system RAM because video memory was full.</param>
sealed record MemorySample(double PrivateMB, double VramMB, double SpilledMB)
{
    public static MemorySample Max(MemorySample a, MemorySample b) =>
        new(Math.Max(a.PrivateMB, b.PrivateMB), Math.Max(a.VramMB, b.VramMB), Math.Max(a.SpilledMB, b.SpilledMB));
}

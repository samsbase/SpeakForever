using SpeakForever.Configuration;

namespace SpeakForever.Speech;

/// <summary>A model the app offers for download.</summary>
/// <param name="File">File name in the whisper.cpp model repository and the models folder.</param>
/// <param name="DownloadBytes">Exact file size.</param>
/// <param name="Sha256">What the file must hash to; anything else is discarded.</param>
/// <param name="MemoryGb">
/// Measured with the benchmark (VRAM plus system RAM held between dictations, greedy decoding,
/// with WoW: Forever running on an RTX 5070 Ti).
/// </param>
public sealed record ModelInfo(string File, string Name, string Blurb, long DownloadBytes, string Sha256, double MemoryGb, bool Recommended = false)
{
    public string LocalPath => Path.Combine(AppPaths.Models, File);
    public bool IsInstalled => System.IO.File.Exists(LocalPath);
    public string Summary => $"{FormatBytes(DownloadBytes)} download · uses about {MemoryGb:F1} GB of memory";

    public static string FormatBytes(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1e9:F1} GB" : $"{bytes / 1e6:F0} MB";
}

using SpeakForever.Configuration;
using SpeakForever.Logging;
using SpeakForever.Net;

namespace SpeakForever.Speech;

/// <summary>The models the app offers, the ones on disk, and downloading them.</summary>
public static class ModelCatalog
{
    // Pinned to one revision of the official whisper.cpp model repository, so a file can't change
    // under us; each download is also checked against its SHA-256.
    const string Repository = "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/";

    public static IReadOnlyList<ModelInfo> All { get; } =
    [
        new("ggml-large-v3-turbo-q5_0.bin", "Turbo",
            "The most accurate, and the lightest of the Turbo models. Fast on a graphics card.",
            574041195, "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", 1.0, Recommended: true),
        new("ggml-large-v3-turbo-q8_0.bin", "Turbo 8-bit",
            "As accurate as Turbo in our tests, but uses more memory.",
            874188075, "317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1", 1.2, Advanced: true),
        new("ggml-large-v3-turbo.bin", "Turbo full precision",
            "As accurate as Turbo in our tests, but uses the most memory.",
            1624555275, "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", 2.0, Advanced: true),
        new("ggml-small.bin", "Small",
            "For PCs without a capable graphics card. About 4× faster than Turbo on the processor, but makes more mistakes.",
            487601967, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", 1.0),
        new("ggml-base.bin", "Base",
            "The smallest and fastest, but makes many more mistakes. For older PCs.",
            147951465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", 0.6),
    ];

    static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", $"SpeakForever/{typeof(ModelCatalog).Assembly.GetName().Version?.ToString(3)}" } },
    };

    public static ModelInfo? Find(string path) =>
        All.FirstOrDefault(m => string.Equals(m.File, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));

    /// <summary>Whisper model files in the models folder, smallest first.</summary>
    public static IReadOnlyList<string> Installed()
    {
        var folder = new DirectoryInfo(AppPaths.Models);
        return folder.Exists
            ? [.. folder.EnumerateFiles("ggml-*.bin").OrderBy(f => f.Length).Select(f => f.FullName)]
            : [];
    }

    /// <summary>The catalog name ("Turbo"), or for a model the user added, "large-v3 (3.1 GB)".</summary>
    public static string DisplayName(string modelPath)
    {
        if (Find(modelPath) is { } known) return known.Name;
        var name = Path.GetFileNameWithoutExtension(modelPath);
        if (name.StartsWith("ggml-", StringComparison.Ordinal)) name = name[5..];
        var file = new FileInfo(modelPath);
        return file.Exists ? $"{name} ({file.Length / 1e9:F1} GB)" : name;
    }

    /// <summary>
    /// Downloads a model, checking its SHA-256 as it arrives. Reports whole percents from 0 to 1.
    /// A cancelled or failed download leaves nothing behind.
    /// </summary>
    /// <exception cref="InvalidDataException">The file didn't match its published checksum.</exception>
    /// <exception cref="TimeoutException">The download stalled.</exception>
    public static async Task DownloadAsync(ModelInfo model, IProgress<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.Models);
        await FileDownload.ToFileAsync(Http, Repository + model.File, model.LocalPath, model.DownloadBytes, model.Sha256, model.Name, progress, ct)
            .ConfigureAwait(false);
        Log.Info($"Downloaded {model.Name} ({ModelInfo.FormatBytes(model.DownloadBytes)}) and checked it.");
    }
}

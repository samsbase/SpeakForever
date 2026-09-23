using System.Buffers;
using System.Security.Cryptography;
using VoiceForever.Configuration;
using VoiceForever.Logging;

namespace VoiceForever.Speech;

/// <summary>The models the app offers, the ones on disk, and downloading them.</summary>
public static class ModelCatalog
{
    // Pinned to one revision of the official whisper.cpp model repository, so a file can't change
    // under us; each download is also checked against its SHA-256.
    const string Repository = "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/";

    const int ChunkBytes = 1 << 20;
    static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    public static IReadOnlyList<ModelInfo> All { get; } =
    [
        new("ggml-large-v3-turbo-q5_0.bin", "Turbo",
            "Best accuracy, and the lightest of the Turbo models. Fast with a graphics card.",
            574041195, "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", 1.0, Recommended: true),
        new("ggml-large-v3-turbo-q8_0.bin", "Turbo 8-bit",
            "Same results as Turbo in testing, using more memory.",
            874188075, "317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1", 1.2),
        new("ggml-large-v3-turbo.bin", "Turbo full precision",
            "Same results as Turbo in testing, using the most memory.",
            1624555275, "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", 2.0),
        new("ggml-small.bin", "Small",
            "For PCs without a capable graphics card: about 4x faster than Turbo on the processor, with more mistakes.",
            487601967, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", 1.0),
        new("ggml-base.bin", "Base",
            "Smallest and fastest, but makes many more mistakes. For older PCs.",
            147951465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", 0.6),
    ];

    static readonly HttpClient Http = new() { DefaultRequestHeaders = { { "User-Agent", "VoiceForever" } } };

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
    /// Downloads a model to a .part file, checking its SHA-256 as it arrives, and only then gives it
    /// its real name. Reports whole percents from 0 to 1. A cancelled or failed download leaves nothing behind.
    /// </summary>
    /// <exception cref="InvalidDataException">The file didn't match its published checksum.</exception>
    /// <exception cref="TimeoutException">No data arrived for <see cref="StallTimeout"/>.</exception>
    public static async Task DownloadAsync(ModelInfo model, IProgress<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.Models);
        var partial = model.LocalPath + ".part";
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkBytes);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            using var response = await Http.GetAsync(Repository + model.File, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                // Reserving the whole size up front keeps the file in one piece on disk; unbuffered
                // async writes, since every write is already a full megabyte.
                var target = new FileStream(partial, new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    PreallocationSize = model.DownloadBytes,
                    BufferSize = 0,
                });
                await using (target.ConfigureAwait(false))
                {
                    long received = 0;
                    int reported = -1, read;
                    do
                    {
                        // Network reads arrive a few KB at a time: fill the chunk first, so the file
                        // write and hash update happen once per megabyte rather than per packet.
                        int filled = 0;
                        do
                        {
                            stall.CancelAfter(StallTimeout);
                            read = await source.ReadAsync(buffer.AsMemory(filled, ChunkBytes - filled), stall.Token).ConfigureAwait(false);
                            filled += read;
                        }
                        while (read > 0 && filled < ChunkBytes);

                        hash.AppendData(buffer, 0, filled);
                        await target.WriteAsync(buffer.AsMemory(0, filled), ct).ConfigureAwait(false);
                        received += filled;
                        // Whole percents only: each report is a UI update, and thousands a second hang the window.
                        int percent = (int)(received * 100 / model.DownloadBytes);
                        if (percent != reported)
                        {
                            reported = percent;
                            progress.Report(percent / 100.0);
                        }
                    }
                    while (read > 0);
                }
            }
            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (actual != model.Sha256)
                throw new InvalidDataException($"{model.Name} didn't match its published checksum, so it was discarded. Try again.");
            File.Move(partial, model.LocalPath, overwrite: true);
            Log.Info($"Downloaded {model.Name} ({ModelInfo.FormatBytes(model.DownloadBytes)}), checksum verified.");
        }
        catch (OperationCanceledException) when (stall.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The download of {model.Name} stalled: nothing arrived for {StallTimeout.TotalSeconds:F0} seconds.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(partial)) File.Delete(partial);
        }
    }
}

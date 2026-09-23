using System.Buffers;
using System.Security.Cryptography;

namespace SpeakForever.Net;

/// <summary>Downloads a large file whose size and SHA-256 are known in advance.</summary>
static class FileDownload
{
    const int ChunkBytes = 1 << 20;
    static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Downloads to a .part file, checking the SHA-256 as it arrives, and only then gives it its
    /// real name. Reports whole percents from 0 to 1. A cancelled or failed download leaves nothing behind.
    /// </summary>
    /// <param name="name">What's being downloaded, for error messages.</param>
    /// <exception cref="InvalidDataException">The file didn't match <paramref name="sha256"/>.</exception>
    /// <exception cref="TimeoutException">No data arrived for <see cref="StallTimeout"/>.</exception>
    public static async Task ToFileAsync(HttpClient http, string url, string path, long bytes, string sha256, string name,
                                         IProgress<double> progress, CancellationToken ct)
    {
        var partial = path + ".part";
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkBytes);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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
                    PreallocationSize = bytes,
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
                        int percent = (int)(received * 100 / bytes);
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
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{name} didn't match its published checksum, so it was discarded. Try again.");
            File.Move(partial, path, overwrite: true);
        }
        catch (OperationCanceledException) when (stall.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The download of {name} stalled: nothing arrived for {StallTimeout.TotalSeconds:F0} seconds.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(partial)) File.Delete(partial);
        }
    }
}

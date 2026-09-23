using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpeakForever.Configuration;
using SpeakForever.Net;

namespace SpeakForever.Updates;

/// <summary>
/// Asks GitHub for the latest release. One small request each time; GitHub's ETag makes repeat
/// checks with nothing new free (a 304 doesn't count against its hourly limit of 60 per address).
/// </summary>
public sealed class UpdateChecker
{
    static readonly Assembly App = typeof(UpdateChecker).Assembly;

    /// <summary>"owner/repo", stamped into the build from Directory.Build.props.</summary>
    public static string Repository { get; } =
        App.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "GitHubRepository")?.Value ?? "";

    /// <summary>This build's version, as major.minor.patch.</summary>
    public static Version CurrentVersion { get; } = Normalise(App.GetName().Version ?? new Version(0, 0, 0));

    static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders =
        {
            { "User-Agent", $"SpeakForever/{CurrentVersion.ToString(3)}" },
            { "Accept", "application/vnd.github+json" },
        },
    };

    readonly Lock gate = new();
    string? etag;
    UpdateInfo? latest;

    /// <summary>The GitHub page listing every release.</summary>
    public static string ReleasesUrl => $"https://github.com/{Repository}/releases";

    /// <summary>
    /// True when this copy was put here by the installer (its uninstaller is beside it), so a new
    /// installer can update it in place; a build run from anywhere else only links to the release.
    /// </summary>
    public static bool CanInstallHere { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    /// <summary>The newer release, or null if this is the latest (or nothing is published yet).</summary>
    /// <exception cref="HttpRequestException">GitHub couldn't be reached, or refused.</exception>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (Repository.Length == 0) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        lock (gate)
        {
            if (etag is not null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
        }
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            lock (gate) return latest;
        }
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // no releases published yet
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        Release? release;
        await using (body.ConfigureAwait(false))
            release = await JsonSerializer.DeserializeAsync(body, ReleaseJson.Default.Release, ct).ConfigureAwait(false);
        var update = NewerThan(release, CurrentVersion);
        lock (gate)
        {
            etag = response.Headers.ETag?.ToString();
            latest = update;
        }
        return update;
    }

    /// <summary>
    /// The release, if it's a published (not draft or pre-release) version newer than
    /// <paramref name="current"/>, whose page is on GitHub. Its installer comes along if it was
    /// uploaded to this repository's release with a SHA-256, so the app can check what it runs.
    /// </summary>
    internal static UpdateInfo? NewerThan(Release? release, Version current)
    {
        if (release is not { Draft: false, Prerelease: false }
            || !release.HtmlUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            || !Version.TryParse(release.TagName.TrimStart('v', 'V'), out var parsed)
            || Normalise(parsed) <= Normalise(current))
            return null;
        var version = Normalise(parsed);
        var installer = release.Assets?.FirstOrDefault(a =>
            a.Name.Equals($"SpeakForever-Setup-{version.ToString(3)}.exe", StringComparison.OrdinalIgnoreCase)
            && a.Url.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase)
            && a.Size > 0
            && a.Digest?.StartsWith("sha256:", StringComparison.Ordinal) == true);
        return new UpdateInfo(version, release.HtmlUrl, installer is null ? null : new(installer.Url, installer.Size, installer.Digest!["sha256:".Length..]));
    }

    /// <summary>Downloads the update's installer, checking its SHA-256, and returns where it is.</summary>
    /// <exception cref="InvalidDataException">It didn't match its published checksum.</exception>
    /// <exception cref="TimeoutException">The download stalled.</exception>
    public static async Task<string> DownloadInstallerAsync(UpdateInfo update, IProgress<double> progress, CancellationToken ct)
    {
        var installer = update.Installer ?? throw new InvalidOperationException("This release has no installer the app can run.");
        DeleteDownloads();
        Directory.CreateDirectory(AppPaths.Updates);
        var path = Path.Combine(AppPaths.Updates, $"SpeakForever-Setup-{update.Version.ToString(3)}.exe");
        await FileDownload.ToFileAsync(Http, installer.Url, path, installer.Bytes, installer.Sha256,
                                       $"Speak Forever {update.Version.ToString(3)}", progress, ct).ConfigureAwait(false);
        return path;
    }

    /// <summary>
    /// Runs a downloaded installer over this copy, showing only its progress. The app should close
    /// straight after: the installer closes it if it's still running, and opens the new version when done.
    /// </summary>
    public static void StartInstaller(string path)
    {
        // No trailing backslash: before a closing quote it would escape it.
        var folder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        using var _ = Process.Start(new ProcessStartInfo(path)
        {
            ArgumentList = { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/RELAUNCH=1", $"/DIR={folder}" },
        });
    }

    /// <summary>Removes installers downloaded earlier, which have done their job.</summary>
    public static void DeleteDownloads()
    {
        try
        {
            if (Directory.Exists(AppPaths.Updates)) Directory.Delete(AppPaths.Updates, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still in use by the installer that just ran; next time.
        }
    }

    // "1.2" and "1.2.0.0" both become 1.2.0, so they compare equal.
    static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>The fields of GitHub's release JSON the app uses.</summary>
    internal sealed record Release(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        bool Draft,
        bool Prerelease,
        IReadOnlyList<Asset>? Assets = null);

    /// <summary>A file attached to a release. GitHub's digest is "sha256:" and the hex hash.</summary>
    internal sealed record Asset(
        string Name,
        [property: JsonPropertyName("browser_download_url")] string Url,
        long Size,
        string? Digest);
}

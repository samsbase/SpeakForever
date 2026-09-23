using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// <paramref name="current"/>, whose page is on GitHub: the app opens that link in the browser.
    /// </summary>
    internal static UpdateInfo? NewerThan(Release? release, Version current) =>
        release is { Draft: false, Prerelease: false }
        && release.HtmlUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
        && Version.TryParse(release.TagName.TrimStart('v', 'V'), out var version)
        && Normalise(version) > Normalise(current)
            ? new UpdateInfo(Normalise(version), release.HtmlUrl)
            : null;

    // "1.2" and "1.2.0.0" both become 1.2.0, so they compare equal.
    static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>The fields of GitHub's release JSON the app uses.</summary>
    internal sealed record Release(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        bool Draft,
        bool Prerelease);
}

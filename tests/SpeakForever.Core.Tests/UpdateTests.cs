using System.Text.Json;
using SpeakForever.Updates;

namespace SpeakForever.Core.Tests;

/// <summary>Deciding whether a release is newer, and which of its files to install.</summary>
public sealed class UpdateTests
{
    static UpdateChecker.Release Release(string tag, bool draft = false, bool prerelease = false, string url = "https://github.com/o/r/releases/tag/x",
                                         params UpdateChecker.Asset[] assets) =>
        new(tag, url, draft, prerelease, assets);

    static UpdateChecker.Asset Installer(string version = "1.2.0", string? repository = null, string? digest = "sha256:ABC123") =>
        new($"SpeakForever-Setup-{version}.exe",
            $"https://github.com/{repository ?? UpdateChecker.Repository}/releases/download/v{version}/SpeakForever-Setup-{version}.exe", 1000, digest);

    [Theory]
    [InlineData("v1.0.1", "1.0.1")]
    [InlineData("1.1", "1.1.0")]
    [InlineData("V2.0.0", "2.0.0")]
    public void ANewerReleaseIsOffered(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.NewerThan(Release(tag), new Version(1, 0, 0, 0))?.Version);

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.9")]
    [InlineData("nightly")]
    public void TheSameOlderOrUnreadableVersionIsNot(string tag) => Assert.Null(UpdateChecker.NewerThan(Release(tag), new Version(1, 0, 0, 0)));

    [Fact]
    public void DraftsPreReleasesAndLinksOffGitHubAreIgnored()
    {
        var current = new Version(1, 0, 0);
        Assert.Null(UpdateChecker.NewerThan(Release("v2.0.0", draft: true), current));
        Assert.Null(UpdateChecker.NewerThan(Release("v2.0.0", prerelease: true), current));
        Assert.Null(UpdateChecker.NewerThan(Release("v2.0.0", url: "https://example.com/installer.exe"), current));
    }

    [Fact]
    public void GitHubsReleaseJsonIsRead()
    {
        var json = """
            { "tag_name": "v1.2.0", "html_url": "https://github.com/o/r/releases/tag/v1.2.0", "draft": false, "prerelease": false,
              "assets": [ { "name": "SpeakForever-Setup-1.2.0.exe", "size": 76319435, "digest": "sha256:ed55",
                            "browser_download_url": "https://github.com/REPO/releases/download/v1.2.0/SpeakForever-Setup-1.2.0.exe" } ] }
            """.Replace("REPO", UpdateChecker.Repository, StringComparison.Ordinal);
        var release = JsonSerializer.Deserialize(json, ReleaseJson.Default.Release);
        var update = UpdateChecker.NewerThan(release, new Version(1, 0, 0));
        Assert.Equal("https://github.com/o/r/releases/tag/v1.2.0", update?.PageUrl);
        Assert.Equal(new UpdateInfo.Download(
            $"https://github.com/{UpdateChecker.Repository}/releases/download/v1.2.0/SpeakForever-Setup-1.2.0.exe", 76319435, "ed55"), update?.Installer);
    }

    [Fact]
    public void TheInstallerComesWithItsSizeAndChecksum()
    {
        var installer = UpdateChecker.NewerThan(Release("v1.2", assets: [new("notes.txt", "https://github.com/x", 1, null), Installer()]), new Version(1, 0, 0))?.Installer;
        Assert.NotNull(installer);
        Assert.Equal(1000, installer.Bytes);
        Assert.Equal("ABC123", installer.Sha256);
        Assert.EndsWith("/SpeakForever-Setup-1.2.0.exe", installer.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstallerFromElsewhereWithoutAChecksumOrForAnotherVersionIsNotRun()
    {
        var current = new Version(1, 0, 0);
        Assert.Null(UpdateChecker.NewerThan(Release("v1.2.0", assets: Installer(repository: "someone/else")), current)!.Installer);
        Assert.Null(UpdateChecker.NewerThan(Release("v1.2.0", assets: Installer(digest: null)), current)!.Installer);
        Assert.Null(UpdateChecker.NewerThan(Release("v1.2.0", assets: Installer(version: "1.1.0")), current)!.Installer);
        Assert.Null(UpdateChecker.NewerThan(Release("v1.2.0"), current)!.Installer);
    }

    [Fact]
    public void TheBuildKnowsItsRepositoryAndVersion()
    {
        Assert.Matches("^[^/]+/[^/]+$", UpdateChecker.Repository);
        Assert.Equal(3, UpdateChecker.CurrentVersion.ToString().Split('.').Length);
    }
}

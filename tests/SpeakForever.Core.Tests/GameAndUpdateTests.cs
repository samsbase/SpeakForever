using System.Text.Json;
using SpeakForever.Configuration;
using SpeakForever.Game;
using SpeakForever.Updates;

namespace SpeakForever.Core.Tests;

/// <summary>Finding WoW: Forever in a Battle.net install, recognising it, and deciding whether a release is newer.</summary>
public sealed class GameAndUpdateTests : IDisposable
{
    // A World of Warcraft folder laid out the way Battle.net does it.
    readonly string wow = Path.Combine(Path.GetTempPath(), "SpeakForeverTests", Guid.NewGuid().ToString("N"), "World of Warcraft");

    public GameAndUpdateTests()
    {
        Flavour("_retail_", "wow", "Wow.exe");
        Flavour("_beta_", "wow_beta", "WowB.exe"); // the retail beta: same exe name as Forever's
        Flavour("_classic_beta_", "wow_classic_beta", "WowB.exe");
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(wow)!, recursive: true);

    string Flavour(string folder, string product, string exe)
    {
        var dir = Path.Combine(wow, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".flavor.info"), $"Product Flavor!STRING:0\n{product}\n");
        File.WriteAllBytes(Path.Combine(dir, exe), []);
        return dir;
    }

    string Forever => Path.Combine(wow, "_classic_beta_");

    [Fact]
    public void FindsForeverByItsProductNotItsExeName() => Assert.Equal(Forever, GameInstall.FindIn([wow]));

    [Fact]
    public void FindsNothingWithoutAForeverFlavour()
    {
        Directory.Delete(Forever, recursive: true);
        Assert.Null(GameInstall.FindIn([wow, Path.Combine(wow, "missing")]));
    }

    [Fact]
    public void ResolvesTheGameFolderItsExeOrTheWowFolderAboveIt()
    {
        Assert.Equal(Forever, GameInstall.Resolve(Forever));
        Assert.Equal(Forever, GameInstall.Resolve(Forever + Path.DirectorySeparatorChar));
        Assert.Equal(Forever, GameInstall.Resolve(Path.Combine(Forever, "WowB.exe")));
        Assert.Equal(Forever, GameInstall.Resolve(wow));
    }

    [Fact]
    public void RefusesAFolderWithNoGameInIt()
    {
        var empty = Path.Combine(wow, "Interface");
        Directory.CreateDirectory(empty);
        Assert.Null(GameInstall.Resolve(empty));
        Assert.Null(GameInstall.Resolve(Path.Combine(wow, "nowhere")));
    }

    [Fact]
    public void TheGameIsMatchedByWhereItRunsFromOnceTheFolderIsKnown()
    {
        var cfg = new Config { GameFolder = Forever };
        Assert.True(cfg.IsGame("WowB", Path.Combine(Forever, "WowB.exe")));
        Assert.False(cfg.IsGame("WowB", Path.Combine(wow, "_beta_", "WowB.exe"))); // the retail beta
        Assert.True(cfg.IsGame("WowB", "")); // path unreadable: falls back to the name
    }

    [Fact]
    public void WithoutAFolderTheGameIsMatchedByName()
    {
        var cfg = new Config();
        Assert.True(cfg.IsGame("wowb", @"D:\anywhere\WowB.exe"));
        Assert.False(cfg.IsGame("notepad", @"C:\Windows\notepad.exe"));
    }

    [Fact]
    public async Task SettingTheFolderResolvesAndSavesIt()
    {
        TestSetup.ResetSettingsFile();
        var ct = TestContext.Current.CancellationToken;
        await using var engine = TestSetup.NewEngine();
        Assert.NotNull(await engine.SetGameFolderAsync(Path.Combine(wow, "Interface"), ct));
        Assert.Null(await engine.SetGameFolderAsync(wow, ct));
        Assert.True(engine.GameFound);
        Assert.Equal(Forever, (await Config.LoadOrCreateAsync(ct)).GameFolder);
    }

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

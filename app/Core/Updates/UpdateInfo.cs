namespace SpeakForever.Updates;

/// <summary>A newer release: its version, the GitHub page for it, and its installer if the app can run it.</summary>
public sealed record UpdateInfo(Version Version, string PageUrl, UpdateInfo.Download? Installer = null)
{
    /// <summary>A release file, with the size and SHA-256 GitHub publishes for it.</summary>
    public sealed record Download(string Url, long Bytes, string Sha256);
}

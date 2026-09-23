using Microsoft.Win32;

namespace SpeakForever.Game;

/// <summary>
/// Finding WoW: Forever on disk. Battle.net installs every WoW flavour into one "World of
/// Warcraft" folder, each in a subfolder (_retail_, _classic_beta_, ...) with a .flavor.info file
/// naming its product. The game is identified by that product, not by folder or exe name: the
/// retail beta's WowB.exe has the same name as WoW: Forever's.
/// </summary>
public static class GameInstall
{
    /// <summary>
    /// Battle.net products that are WoW: Forever. The beta ships as the Classic beta product; add
    /// the live product here when it launches. Users can also point the app at any folder.
    /// </summary>
    static readonly string[] ForeverProducts = ["wow_classic_beta"];

    const string FlavorFile = ".flavor.info";

    /// <summary>The WoW: Forever folder (the one with the game's .exe) on this PC, or null. Reads the registry and disk.</summary>
    public static string? Detect() => FindIn(WowFolders());

    /// <summary>The first WoW: Forever flavour folder under any of these World of Warcraft folders, or null.</summary>
    public static string? FindIn(IEnumerable<string> wowFolders) =>
        wowFolders.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root, "_*_"))
            .FirstOrDefault(dir => IsForever(dir) && HasGame(dir));

    /// <summary>
    /// Where the user pointed: the game's folder, its .exe, or the World of Warcraft folder above
    /// it. Returns the folder with the game in it, or null if there isn't one there.
    /// </summary>
    public static string? Resolve(string path)
    {
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(path)) return null;
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return HasGame(path) ? path : FindIn([path]);
    }

    /// <summary>The folder holds a WoW client: Wow.exe, WowB.exe, WowClassic.exe and so on.</summary>
    public static bool HasGame(string folder) =>
        folder.Length > 0 && Directory.Exists(folder) && Directory.EnumerateFiles(folder, "Wow*.exe").Any();

    /// <summary>The flavour folder's Battle.net product is WoW: Forever.</summary>
    static bool IsForever(string folder)
    {
        var info = Path.Combine(folder, FlavorFile);
        if (!File.Exists(info)) return false;
        try
        {
            // "Product Flavor!STRING:0" then the product, e.g. "wow_classic_beta".
            return File.ReadLines(info).Skip(1).Any(line => ForeverProducts.Contains(line.Trim(), StringComparer.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Where Battle.net has installed WoW: its uninstall entries, its own setting, then the default place.</summary>
    static IEnumerable<string> WowFolders()
    {
        using var uninstall = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall is not null)
        {
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var app = uninstall.OpenSubKey(name);
                if (app?.GetValue("DisplayName") is string display && display.StartsWith("World of Warcraft", StringComparison.OrdinalIgnoreCase)
                    && app.GetValue("InstallLocation") is string location && location.Length > 0)
                    yield return location;
            }
        }
        using var blizzard = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft");
        if (blizzard?.GetValue("InstallPath") is string flavour && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(flavour)) is { } root)
            yield return root;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "World of Warcraft");
    }
}

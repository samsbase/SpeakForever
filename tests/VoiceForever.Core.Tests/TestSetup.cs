using System.Runtime.CompilerServices;
using VoiceForever.Configuration;
using VoiceForever.Input;

// Several tests read and write the settings file; one at a time keeps them from racing each other.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace VoiceForever.Core.Tests;

static class TestSetup
{
    /// <summary>
    /// Runs before anything touches AppPaths: every test's settings and models live in a fresh
    /// temporary folder, never the user's real %LOCALAPPDATA%\VoiceForever.
    /// </summary>
    [ModuleInitializer]
    internal static void UseTemporaryDataFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "VoiceForeverTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("VOICEFOREVER_DATA", folder);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { } // a file still open; the temp folder is cleaned up eventually anyway
        };
    }

    /// <summary>An engine with sounds off, for driving with simulated controller input.</summary>
    public static Engine NewEngine() => new(new Config { Sounds = false });

    /// <summary>Presses and releases a chord the way the controller loop would report it.</summary>
    public static void Press(this Engine engine, string chord)
    {
        var c = Chord.Parse(chord);
        engine.OnButtons(c.Modifiers, c.Modifiers | c.Key, probe: true);
        engine.OnButtons(c.Modifiers | c.Key, 0, probe: true);
    }

    /// <summary>Holds the right stick in one place; a held stick is seen on many polls.</summary>
    public static void Stick(this Engine engine, float x, float y)
    {
        engine.OnRightStick(x, y);
        engine.OnRightStick(x, y);
    }

    /// <summary>Points at Chat's position on the Main Menu page (down-left), then lets go.</summary>
    public static void PickDownLeft(this Engine engine)
    {
        engine.Stick(-0.7f, -0.7f);
        engine.Stick(0f, 0f);
    }

    public static void ResetSettingsFile()
    {
        if (File.Exists(AppPaths.Config)) File.Delete(AppPaths.Config);
    }
}

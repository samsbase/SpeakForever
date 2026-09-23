using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using SpeakForever.Configuration;
using SpeakForever.Gui.Views;
using SpeakForever.Logging;

namespace SpeakForever.Gui;

public partial class App : Application
{
    /// <summary>Also set on the installer's Start menu shortcut, so a pinned app and its window are one taskbar item.</summary>
    const string AppUserModelId = "SpeakForever.App";

    Window? window;

    public App()
    {
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        InitializeComponent();
        UnhandledException += (_, e) => Log.Warn($"Unhandled: {e.Exception}");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Warn($"Unobserved task error: {e.Exception.GetBaseException().Message}");
    }

    /// <summary>Loads the settings, builds the engine, and hands both to the window.</summary>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppPaths.MigrateFromOldName();
        Log.ToFile("speakforever.log");
        Engine? engine = null;
        string? configError = null;
        try
        {
            engine = new Engine(await Config.LoadOrCreateAsync());
        }
        catch (Exception e) when (e is JsonException or FormatException or IOException or UnauthorizedAccessException)
        {
            configError = $"Config error in {AppPaths.Config}: {e.Message}";
            Log.Warn(configError);
        }
        window = new MainWindow(engine, configError);
        window.Activate();
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);
}

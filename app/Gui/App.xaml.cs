using System.Text.Json;
using Microsoft.UI.Xaml;
using VoiceForever.Configuration;
using VoiceForever.Gui.Views;
using VoiceForever.Logging;

namespace VoiceForever.Gui;

public partial class App : Application
{
    Window? window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Warn($"Unhandled: {e.Exception}");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Warn($"Unobserved task error: {e.Exception.GetBaseException().Message}");
    }

    /// <summary>Loads the settings, builds the engine, and hands both to the window.</summary>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppPaths.MigrateFromOldName();
        Log.ToFile("voiceforever.log");
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
}

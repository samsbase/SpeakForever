using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpeakForever.Configuration;
using SpeakForever.Gui.Models;
using SpeakForever.Logging;
using SpeakForever.Speech;
using Windows.Foundation;
using Windows.Graphics;

namespace SpeakForever.Gui.Views;

/// <summary>
/// The app window: a status line and three tabs (Dictation, Speech model, Buttons &amp; shortcuts).
/// This file has the window itself and the Dictation tab; the other tabs are in their own partial files.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "Its CancellationTokenSources live for one operation each and are disposed in that operation's finally block; a Window has no Dispose to hang them on.")]
public sealed partial class MainWindow : Window
{
    const int MaxLogLines = 400;
    const int InitialWidth = 780, InitialHeight = 640;
    const int SpeechModelTab = 1;

    readonly Engine? engine;
    readonly ObservableCollection<LogLine> log = [];
    readonly Brush normalBrush = Brush("PaleArcaneBrush");
    readonly Brush warningBrush = Brush("WarningBrush");
    bool updating, heardAnything, shutDown;

    /// <param name="engine">Null when the settings couldn't be loaded; the window then only shows why.</param>
    /// <param name="configError">Why the settings couldn't be loaded.</param>
    public MainWindow(Engine? engine, string? configError)
    {
        InitializeComponent();
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(InitialWidth * scale), (int)(InitialHeight * scale)));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "SpeakForever.ico"));
        StyleTitleBar(scale);

        LogList.ItemsSource = log;
        ModelList.ItemsSource = modelRows;
        Log.Written += OnLogWritten;
        Closed += OnClosed;
        Root.PreviewKeyDown += OnPreviewKeyDown;

        this.engine = engine;
        if (engine is null)
        {
            StatusText.Text = configError;
            ActiveSwitch.IsEnabled = TestMicButton.IsEnabled = false;
            return;
        }
        engine.StateChanged += () => DispatcherQueue.TryEnqueue(UpdateState);
        engine.Transcribed += (text, took, seconds) => DispatcherQueue.TryEnqueue(() => ShowHeard(text, took, seconds));
        engine.PhaseChanged += phase => DispatcherQueue.TryEnqueue(() => Logo.Show(phase));

        Log.Info($"Config: {AppPaths.Config}");
        ShowBindings();
        InitializePauseSlider();
        LoadStartingModel();
        engine.Start();
        UpdateState();
        Root.Loaded += async (_, _) =>
        {
            FitToSpeechModelTab();
            await StartupChecksAsync();
        };
    }

    /// <summary>Our own title row, with the logo; the caption buttons keep Windows' behaviour in our colours.</summary>
    void StyleTitleBar(double scale)
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        var bar = AppWindow.TitleBar;
        bar.PreferredHeightOption = TitleBarHeightOption.Tall;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = ColorHelper.FromArgb(255, 0xB8, 0xCC, 0xFF);
        bar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 0x8A, 0x93, 0xB8);
        bar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 0x2E, 0x3F, 0x8F);
        bar.ButtonHoverForegroundColor = Colors.White;
        bar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 0x1B, 0x25, 0x60);
        bar.ButtonPressedForegroundColor = Colors.White;
        TitleBar.Padding = new Thickness(18, 0, bar.RightInset / scale, 0);
    }

    /// <summary>
    /// Sizes the window so the Speech model tab, the tallest, fits without scrolling; the other
    /// tabs stretch to match. Capped at the screen's working area.
    /// </summary>
    void FitToSpeechModelTab()
    {
        if (ModelPage.Content is not FrameworkElement content || DictationPage.ActualHeight <= 0) return;
        // The tab's content isn't laid out while it's hidden, so measure it at the width it will get.
        content.Measure(new Size(DictationPage.ActualWidth, double.PositiveInfinity));
        double extra = content.DesiredSize.Height - DictationPage.ActualHeight;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int height = (int)Math.Min(work.Height, AppWindow.Size.Height + Math.Ceiling(extra * Root.XamlRoot.RasterizationScale));
        AppWindow.Resize(new SizeInt32(AppWindow.Size.Width, height));
    }

    static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    // ---- State ----------------------------------------------------------------------------

    /// <summary>Everything that follows the engine's state. Cheap: no disk access here.</summary>
    void UpdateState()
    {
        if (engine is null) return;
        updating = true;
        ActiveSwitch.IsOn = engine.IsRunning;
        updating = false;

        var cfg = engine.Config;
        StatusText.Text = engine.StartError is { } error ? error
            : !engine.IsRunning ? "Paused: controller and shortcut are ignored"
            : engine.ControllerSlot < 0 ? (cfg.KeyboardShortcut is { } key ? $"No controller · keyboard shortcut {key} is ready" : "Waiting for a controller...")
            : engine.ChatOpen ? $"Chat open · press {cfg.DictateChord} to dictate"
            : $"Controller connected · open chat with {cfg.OpenChatChord}";
        ActiveSwitch.IsEnabled = recording == Recording.None;

        UpdateButtonsTab();
        UpdateSpeechModelTab();
        UpdateSettingsTab();
    }

    void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int tab = sender.Items.IndexOf(sender.SelectedItem);
        DictationPage.Visibility = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModelPage.Visibility = tab == SpeechModelTab ? Visibility.Visible : Visibility.Collapsed;
        ButtonsPage.Visibility = tab == 2 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tab == SettingsTab ? Visibility.Visible : Visibility.Collapsed;
        if (tab == 0 && log.Count > 0) LogList.ScrollIntoView(log[^1]); // lines logged while hidden didn't scroll it
        if (tab == SpeechModelTab) RefreshModels(); // picks up models added or removed outside the app
    }

    void ActiveSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (updating || engine is null) return;
        if (ActiveSwitch.IsOn) engine.Start();
        else engine.Stop();
    }

    // ---- Dictation tab --------------------------------------------------------------------

    void ShowHeard(string text, TimeSpan took, double seconds)
    {
        heardAnything = true;
        LastHeardText.Text = text.Length > 0 ? text : "(nothing recognisable)";
        var model = engine?.LoadedModel is { } path ? ModelCatalog.DisplayName(path) : "?";
        LastHeardMeta.Text = $"{seconds:F1}s of speech · transcribed in {took.TotalMilliseconds:F0} ms by {model}";
    }

    void OnLogWritten(string line, bool warning) => DispatcherQueue.TryEnqueue(() =>
    {
        log.Add(new LogLine(line, warning ? warningBrush : normalBrush));
        if (log.Count > MaxLogLines) log.RemoveAt(0);
        if (DictationPage.Visibility == Visibility.Visible) LogList.ScrollIntoView(log[^1]);
    });

    /// <summary>
    /// Closing hides the window at once, then keeps it until the engine has shut down (a dictation
    /// in progress, the model's native memory) and any pending setting is saved, and closes again.
    /// </summary>
    async void OnClosed(object sender, WindowEventArgs args)
    {
        if (shutDown) return;
        args.Handled = true;
        AppWindow.Hide();
        Log.Written -= OnLogWritten;
        stopUpdates.Cancel();
        foreach (var cancel in downloads.Values) cancel.Cancel();
        await SavePauseAsync();
        if (engine is not null) await engine.DisposeAsync();
        stopUpdates.Dispose();
        shutDown = true;
        // Queued rather than called here: closing again from inside the handling of the first
        // close leaves the process running after the window has gone.
        DispatcherQueue.TryEnqueue(Close);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);
}

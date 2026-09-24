using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpeakForever.Configuration;
using SpeakForever.Gui.Controls;
using SpeakForever.Gui.Models;
using SpeakForever.Input;
using SpeakForever.Logging;
using SpeakForever.Presentation;
using SpeakForever.Speech;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;

namespace SpeakForever.Gui.Views;

/// <summary>
/// The app window: first-run setup, then four tabs (Home, Voice model, Controls, Settings).
/// This file has the window itself and the Home tab; the rest are in their own partial files.
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
    bool updating, shutDown;
    ButtonStyle? shownStyle; // the controller whose icons are showing
    DictationPhase phase;

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
        AdvancedModelList.ItemsSource = advancedRows;
        Log.Written += OnLogWritten;
        Closed += OnClosed;
        Root.PreviewKeyDown += OnPreviewKeyDown;
        Root.PreviewKeyUp += OnPreviewKeyUp;

        this.engine = engine;
        if (engine is null)
        {
            ShowStatus("Can't load settings", configError ?? "", StatusTone.Problem);
            ActiveSwitch.IsEnabled = TestMicButton.IsEnabled = HomeTestMicButton.IsEnabled = false;
            return;
        }
        engine.StateChanged += () => DispatcherQueue.TryEnqueue(UpdateState);
        engine.Transcribed += (text, took, seconds) => DispatcherQueue.TryEnqueue(() => ShowHeard(text, took, seconds));
        engine.PhaseChanged += phase => DispatcherQueue.TryEnqueue(() =>
        {
            this.phase = phase;
            Logo.Show(phase);
            UpdateState();
            UpdateOverlay();
        });
        engine.TooLong += leftOut => DispatcherQueue.TryEnqueue(() => ShowTooLong(leftOut));
        Recorder.Level += level => DispatcherQueue.TryEnqueue(() => overlay?.ShowLevel(level));

        Log.Info($"Settings file: {AppPaths.Config}");
        ShowBindings();
        InitializePauseSlider();
        InitializeSettingsTab();
        LoadStartingModel();
        ShowSetupIfNeeded();
        engine.Start();
        RefreshMics(); // and the rest of the state
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) RefreshMics();
        };
        Root.Loaded += async (_, _) =>
        {
            FitToTallestTab();
            StartupChecks();
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
    /// Sizes the window so that every tab fits without scrolling (the first-run steps too, while
    /// they show); the shorter tabs stretch to match. Capped at the screen's working area.
    /// </summary>
    void FitToTallestTab()
    {
        double width = Body.ActualWidth - Body.Padding.Left - Body.Padding.Right;
        if (width <= 0) return;
        // Hidden tabs aren't laid out, so each is measured at the width it will get. Home, which
        // isn't a scrolling tab, counts too: its activity log only takes whatever height is left.
        UIElement?[] tabs = [DictationPage, ModelPage.Content as UIElement, ButtonsPage.Content as UIElement, SettingsPage.Content as UIElement,
                             inSetup ? SetupPage.Content as UIElement : null];
        double tallest = 0;
        foreach (var tab in tabs.OfType<UIElement>())
        {
            tab.Measure(new Size(width, double.PositiveInfinity));
            tallest = Math.Max(tallest, tab.DesiredSize.Height);
        }
        double extra = tallest - (Body.ActualHeight - TabsBar.ActualHeight); // the tab bar is hidden, so 0 high, during setup
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
        if (engine.ButtonStyle != shownStyle) ShowBindings(); // a different kind of controller: its own icons
        var status = HomeStatus.Of(engine.StartError, engine.IsRunning, phase, engine.LoadedModel is not null, engine.IsLoadingModel,
                                   engine.ControllerSlot >= 0, engine.ChatOpen, cfg.KeyboardShortcut);
        ShowStatus(status.Headline, status.Detail, status.Tone);
        ControllerText.Text = !engine.IsRunning ? "" : engine.ControllerSlot >= 0 ? engine.ControllerName ?? "Controller connected" : "No controller";
        ActiveSwitch.IsEnabled = recording == Recording.None;
        GlanceModelText.Text = engine.LoadedModel is { } model ? ModelCatalog.DisplayName(model) : engine.IsLoadingModel ? "Loading…" : "None yet";
        UpdateMic();
        GlanceShortcutText.Text = cfg.KeyboardShortcut ?? "Off";

        UpdateButtonsTab();
        UpdateSpeechModelTab();
        UpdateSetup();
    }

    /// <summary>The Home tab's status: "{0}" and "{1}" in the detail are drawn as the open-chat and dictate buttons.</summary>
    void ShowStatus(string headline, string detail, StatusTone tone)
    {
        StatusHeadline.Text = headline;
        if (engine is null) ButtonPrompt.Fill(StatusText, detail);
        else ButtonPrompt.Fill(StatusText, detail, engine.ButtonStyle, Chord.Parse(engine.Config.OpenChatChord), Chord.Parse(engine.Config.DictateChord));
        StatusDot.Fill = StatusRing.Stroke = StatusIcon.Foreground = Brush($"Tone{tone}Brush");
        StatusRing.Fill = Brush($"Tone{tone}FillBrush");
        StatusIcon.Glyph = tone == StatusTone.Problem ? "\uE7BA" : "\uE720"; // warning, or the microphone
    }

    void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!inSetup) ShowTab(sender.Items.IndexOf(sender.SelectedItem));
    }

    void ShowTab(int tab)
    {
        if (Tabs.SelectedItem != Tabs.Items[tab]) Tabs.SelectedItem = Tabs.Items[tab]; // comes back here through SelectionChanged
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

    // ---- Home tab --------------------------------------------------------------------

    void ShowHeard(string text, TimeSpan took, double seconds)
    {
        LastHeardHint.Visibility = Visibility.Collapsed;
        LastHeardText.Visibility = Visibility.Visible;
        LastHeardText.Text = text.Length > 0 ? text : "(nothing recognisable)";
        CopyHeardButton.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        LastHeardMeta.Visibility = Visibility.Visible;
        var model = engine?.LoadedModel is { } path ? ModelCatalog.DisplayName(path) : "?";
        LastHeardMeta.Text = $"{seconds:F1} s of speech · transcribed in {took.TotalMilliseconds:F0} ms by {model}";
        LastHeardTooLong.Visibility = Visibility.Collapsed; // until it's copied, and turns out not to fit
    }

    void CopyHeardButton_Click(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(LastHeardText.Text);
        Clipboard.SetContent(data);
    }

    /// <summary>The activity log is for when something goes wrong, so it stays folded away until asked for.</summary>
    void ActivityToggle_Click(object sender, RoutedEventArgs e)
    {
        bool show = LogBorder.Visibility != Visibility.Visible;
        LogBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ActivityToggle.Content = show ? "Hide activity" : "Show activity";
        if (show && log.Count > 0) LogList.ScrollIntoView(log[^1]);
    }

    void OnLogWritten(string line, bool warning) => DispatcherQueue.TryEnqueue(() =>
    {
        log.Add(new LogLine(line, warning ? warningBrush : normalBrush));
        if (log.Count > MaxLogLines) log.RemoveAt(0);
        if (DictationPage.Visibility == Visibility.Visible && LogBorder.Visibility == Visibility.Visible) LogList.ScrollIntoView(log[^1]);
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
        CloseOverlay();
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

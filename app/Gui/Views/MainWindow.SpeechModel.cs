using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using VoiceForever.Logging;
using VoiceForever.Speech;

namespace VoiceForever.Gui.Views;

/// <summary>The Speech model tab: the model list and downloads, the pause slider and the microphone test.</summary>
public sealed partial class MainWindow
{
    static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    readonly Dictionary<string, Download> downloads = new(StringComparer.OrdinalIgnoreCase);
    DispatcherQueueTimer? pauseSave;
    CancellationTokenSource? testFinish;
    bool testingMic;
    (string? Loaded, string? Loading, string Status, bool Testing) shownModels;

    /// <summary>A download in progress, and the row controls showing it (rebuilt with the list).</summary>
    sealed class Download
    {
        public CancellationTokenSource Cancel { get; } = new();
        public ProgressBar? Bar { get; set; }
        public TextBlock? Percent { get; set; }
        public double Progress { get; set; }
    }

    void LoadStartingModel()
    {
        if (engine!.StartingModel() is { } path) _ = engine.LoadModelAsync(path); // logs its own failures
        else if (engine.RemovedModel is null) Log.Info("No speech model yet. Download one on the Speech model tab; Turbo is recommended.");
    }

    void UpdateSpeechModelTab()
    {
        if (engine is null) return;
        TestMicButton.IsEnabled = testingMic || (!engine.IsLoadingModel && engine.LoadedModel is not null);
        // The list reads the models folder, so it's only rebuilt when something it shows has changed.
        var now = (engine.LoadedModel, engine.LoadingModel, engine.ModelStatus, testingMic);
        if (now != shownModels) RefreshModels();
    }

    /// <summary>Rebuilds the model list and the no-model notice from what's on disk now.</summary>
    void RefreshModels()
    {
        if (engine is null) return;
        shownModels = (engine.LoadedModel, engine.LoadingModel, engine.ModelStatus, testingMic);
        bool noModel = engine.LoadedModel is null && !engine.IsLoadingModel;
        ModelStatusText.Text = noModel ? "No model in use yet. Download one below: Turbo is recommended." : engine.ModelStatus;

        var installed = ModelCatalog.Installed();
        ShowModelNotice(noModel, installed.Count > 0);
        ModelList.Children.Clear();
        foreach (var model in ModelCatalog.All)
            AddModelRow(model.Name, model.Recommended, model.Summary, model.Blurb, model.LocalPath, model);

        // Models the user put in the folder themselves.
        foreach (var path in installed.Where(p => ModelCatalog.Find(p) is null))
            AddModelRow(ModelCatalog.DisplayName(path), false, "Added by you", null, path, null);
    }

    /// <summary>Nothing can be dictated without a model, so say so on the tab people land on.</summary>
    /// <param name="installed">Models are on disk, so one is there but failed to load.</param>
    void ShowModelNotice(bool noModel, bool installed)
    {
        ModelNotice.Visibility = noModel ? Visibility.Visible : Visibility.Collapsed;
        if (!noModel) return;
        bool downloading = downloads.Count > 0;
        ModelNoticeTitle.Text = downloading ? "Downloading a speech model"
            : engine!.RemovedModel is not null && !installed ? "Your speech model was removed"
            : installed ? "Speech model not loaded"
            : "Download a speech model to start";
        ModelNoticeText.Text = downloading ? "Dictation starts working as soon as it finishes. Progress is on the Speech model tab."
            : installed ? $"{engine!.ModelStatus}. Pick another on the Speech model tab."
            : engine!.RemovedModel is { } removed ? $"{removed} is no longer on this PC. Download it again, or another model, before you can dictate."
            : "Voice Forever needs a speech model before it can dictate. Turbo is recommended: a 574 MB download, about 1 GB of memory while running.";
        ModelNoticeButton.Content = downloading ? "Show progress" : "Choose a model";
    }

    void ModelNoticeButton_Click(object sender, RoutedEventArgs e) => Tabs.SelectedItem = Tabs.Items[SpeechModelTab];

    void AddModelRow(string name, bool recommended, string summary, string? blurb, string path, ModelInfo? catalogEntry)
    {
        bool first = ModelList.Children.Count == 0;
        var row = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(0, 8, 0, 8),
            BorderBrush = Brush("IndigoBrush"),
            BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = name, FontSize = 15, Foreground = Brush("StarlightBrush"), VerticalAlignment = VerticalAlignment.Center });
        if (recommended) title.Children.Add(RecommendedBadge());
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Text = summary, Style = CaptionStyle, Foreground = Brush("ArcaneBrush") });
        if (blurb is not null) text.Children.Add(new TextBlock { Text = blurb, Style = CaptionStyle });
        row.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 1);
        bool inUse = string.Equals(path, engine!.LoadedModel, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(path, engine.LoadingModel, StringComparison.OrdinalIgnoreCase);

        if (downloads.TryGetValue(path, out var download))
        {
            download.Bar = new ProgressBar { Width = 130, Maximum = 1, Value = download.Progress, VerticalAlignment = VerticalAlignment.Center };
            download.Percent = new TextBlock { Text = $"{download.Progress:P0}", MinWidth = 38, Foreground = Brush("ArcaneBrush"), VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(download.Bar);
            actions.Children.Add(download.Percent);
            actions.Children.Add(PanelButton("Cancel", (_, _) => download.Cancel.Cancel()));
        }
        else if (inUse)
        {
            actions.Children.Add(new TextBlock
            {
                Text = engine.IsLoadingModel ? "Loading..." : "In use",
                FontFamily = CinzelFont,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush("ArcaneBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else if (catalogEntry is null || catalogEntry.IsInstalled)
        {
            var use = PanelButton("Use", async (_, _) => await engine.LoadModelAsync(path));
            use.IsEnabled = !engine.IsLoadingModel && !testingMic;
            actions.Children.Add(use);
            var delete = PanelButton("", async (_, _) => await DeleteModelAsync(name, path));
            delete.Content = new FontIcon { Glyph = "", FontSize = 14 };
            delete.MinWidth = 0;
            ToolTipService.SetToolTip(delete, $"Delete {name}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(delete, $"Delete {name}");
            actions.Children.Add(delete);
        }
        else
        {
            actions.Children.Add(PanelButton("Download", async (_, _) => await DownloadAsync(catalogEntry)));
        }
        row.Children.Add(actions);
        ModelList.Children.Add(row);
    }

    static Style CaptionStyle => (Style)Application.Current.Resources["Caption"];
    static FontFamily CinzelFont => (FontFamily)Application.Current.Resources["Cinzel"];

    static Border RecommendedBadge() => new()
    {
        Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x2E, 0x3F, 0x8F)),
        BorderBrush = Brush("SilverBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(6, 1, 6, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = "Recommended", FontSize = 11, FontFamily = CinzelFont, Foreground = Brush("StarlightBrush") },
    };

    static Button PanelButton(string label, RoutedEventHandler click)
    {
        var button = new Button { Content = label, Style = (Style)Application.Current.Resources["PanelButton"] };
        button.Click += click;
        return button;
    }

    /// <summary>Downloads a model with progress in its row; the first model downloaded is loaded straight away.</summary>
    async Task DownloadAsync(ModelInfo model)
    {
        var download = new Download();
        downloads[model.LocalPath] = download;
        RefreshModels();
        // Created on the UI thread, so reports arrive here; the downloader sends at most 101 of them.
        var progress = new Progress<double>(p =>
        {
            download.Progress = p;
            if (download.Bar is { } bar) bar.Value = p;
            if (download.Percent is { } percent) percent.Text = $"{p:P0}";
        });
        try
        {
            await ModelCatalog.DownloadAsync(model, progress, download.Cancel.Token);
            if (engine!.LoadedModel is null && !engine.IsLoadingModel)
                _ = engine.LoadModelAsync(model.LocalPath); // logs its own failures
        }
        catch (OperationCanceledException)
        {
            Log.Info($"Download of {model.Name} cancelled.");
        }
        catch (Exception e)
        {
            Log.Warn($"Download of {model.Name} failed: {e.Message}");
        }
        finally
        {
            downloads.Remove(model.LocalPath);
            download.Cancel.Dispose();
            RefreshModels();
        }
    }

    async Task DeleteModelAsync(string name, string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            RefreshModels();
            return;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Delete {name}?",
            Content = $"This frees {ModelInfo.FormatBytes(file.Length)} of disk space. You can download it again later.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Keep",
            DefaultButton = ContentDialogButton.Close,
        };
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        catch (COMException)
        {
            return; // another dialog is already open; WinUI allows one at a time
        }
        if (engine!.DeleteModel(path) is { } error) Log.Warn(error);
        RefreshModels();
    }

    // ---- Pause slider -----------------------------------------------------------------------

    void InitializePauseSlider()
    {
        // Hooked up here rather than in XAML: setting Minimum fires ValueChanged mid-load.
        PauseSlider.Value = engine!.Config.SilenceMs / 1000.0;
        PauseText.Text = $"{PauseSlider.Value:F1} s";
        PauseSlider.ValueChanged += PauseSlider_ValueChanged;
        // Dragging fires ValueChanged dozens of times; save once it settles, not on every step.
        pauseSave = DispatcherQueue.CreateTimer();
        pauseSave.Interval = SaveDelay;
        pauseSave.IsRepeating = false;
        pauseSave.Tick += async (_, _) => await SaveConfigAsync();
    }

    void PauseSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        PauseText.Text = $"{e.NewValue:F1} s";
        if (engine is null) return;
        engine.Config.SilenceMs = (int)Math.Round(e.NewValue * 1000);
        pauseSave?.Start(); // restarts the delay if it's already pending
    }

    /// <summary>On close: a pending slider change is saved before the app exits.</summary>
    void SavePauseNow()
    {
        if (pauseSave is not { IsRunning: true }) return;
        pauseSave.Stop();
        // Blocking is acceptable here: the window is closing, and it's one small file.
        engine?.Config.SaveAsync().GetAwaiter().GetResult();
    }

    async Task SaveConfigAsync()
    {
        try
        {
            await engine!.Config.SaveAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save settings: {e.Message}");
        }
    }

    // ---- Microphone test --------------------------------------------------------------------

    async void TestMicButton_Click(object sender, RoutedEventArgs e)
    {
        if (engine is null) return;
        if (testingMic)
        {
            testFinish?.Cancel();
            return;
        }
        testingMic = true;
        testFinish = new CancellationTokenSource();
        TestMicLabel.Text = "Finish";
        UpdateState();
        LastHeardText.Text = "Listening... say something, then pause or press Finish.";
        LastHeardMeta.Text = "";
        try
        {
            var result = await engine.TestMicAsync(testFinish.Token);
            if (result is var (text, took, seconds))
            {
                ShowHeard(text, took, seconds);
                Log.Info($"Mic test: {seconds:F1}s in {took.TotalMilliseconds:F0} ms: \"{text}\"");
            }
            else
            {
                LastHeardText.Text = $"Heard no speech in {engine.Config.NoSpeechTimeoutSeconds}s. Check the microphone.";
            }
        }
        catch (Exception ex)
        {
            LastHeardText.Text = $"Mic test failed: {ex.Message}";
            Log.Warn(LastHeardText.Text);
        }
        finally
        {
            testingMic = false;
            testFinish.Dispose();
            testFinish = null;
            TestMicLabel.Text = "Test microphone";
            UpdateState();
        }
    }
}

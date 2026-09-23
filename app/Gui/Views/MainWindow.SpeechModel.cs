using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SpeakForever.Logging;
using SpeakForever.Presentation;
using SpeakForever.Speech;

namespace SpeakForever.Gui.Views;

/// <summary>The Speech model tab: the model list and downloads, the pause slider and the microphone test.</summary>
public sealed partial class MainWindow
{
    static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    readonly ObservableCollection<ModelRow> modelRows = [];
    readonly Dictionary<string, CancellationTokenSource> downloads = new(StringComparer.OrdinalIgnoreCase);
    DispatcherQueueTimer? pauseSave;
    int? pendingSilenceMs;
    bool testingMic;
    (string? Loaded, string? Loading, string Status, bool Testing) shownModels;

    void LoadStartingModel()
    {
        if (engine!.StartingModel() is { } path) _ = engine.LoadModelAsync(path); // logs its own failures
        else if (engine.RemovedModel is null) Log.Info("No speech model yet. Download one on the Speech model tab (Turbo is recommended).");
    }

    void UpdateSpeechModelTab()
    {
        if (engine is null) return;
        TestMicButton.IsEnabled = !testingMic && !engine.IsLoadingModel && engine.LoadedModel is not null;
        // Refreshing reads the models folder, so it only happens when something it shows has changed.
        var now = (engine.LoadedModel, engine.LoadingModel, engine.ModelStatus, testingMic);
        if (now != shownModels) RefreshModels();
    }

    /// <summary>Brings the model list and the no-model notice up to date with what's on disk now.</summary>
    void RefreshModels()
    {
        if (engine is null) return;
        shownModels = (engine.LoadedModel, engine.LoadingModel, engine.ModelStatus, testingMic);
        bool noModel = engine.LoadedModel is null && !engine.IsLoadingModel;
        ModelStatusText.Text = noModel ? "No speech model yet. Download one below; Turbo is recommended." : engine.ModelStatus;

        var installed = ModelCatalog.Installed();
        ShowModelNotice(noModel, installed.Count > 0);

        // The catalog's models, then any the user put in the folder themselves. Rows are kept while
        // their model stays listed, so a download's progress survives a refresh.
        var listed = ModelCatalog.All.Select(m => (m.LocalPath, m.Name, m.Summary, (string?)m.Blurb, (ModelInfo?)m))
            .Concat(installed.Where(p => ModelCatalog.Find(p) is null)
                .Select(p => (p, ModelCatalog.DisplayName(p), "Added by you", (string?)null, (ModelInfo?)null)))
            .ToList();
        if (!listed.Select(m => m.Item1).SequenceEqual(modelRows.Select(r => r.Path), StringComparer.OrdinalIgnoreCase))
        {
            var existing = modelRows.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
            modelRows.Clear();
            foreach (var (path, name, summary, blurb, entry) in listed)
                modelRows.Add(existing.GetValueOrDefault(path) ?? new ModelRow(path, name, summary, blurb, entry));
        }

        var onDisk = installed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < modelRows.Count; i++)
        {
            var row = modelRows[i];
            row.HasDivider = i > 0;
            row.State = ModelRow.StateOf(row.Path, onDisk.Contains(row.Path), engine.LoadedModel, engine.LoadingModel, downloads.ContainsKey(row.Path));
            row.CanUse = !engine.IsLoadingModel && !testingMic;
        }
    }

    /// <summary>Nothing can be dictated without a model, so say so on the tab people land on.</summary>
    /// <param name="installed">Models are on disk, so one is there but failed to load.</param>
    void ShowModelNotice(bool noModel, bool installed)
    {
        if (!noModel)
        {
            ModelNotice.Show(null);
            return;
        }
        if (downloads.Count > 0)
            ModelNotice.Show("Downloading a speech model", "You can dictate as soon as it finishes. Progress is on the Speech model tab.", "Show progress");
        else if (installed)
            ModelNotice.Show("Your speech model didn't load", $"{engine!.ModelStatus.TrimEnd('.')}. Try another on the Speech model tab.", "Choose a model");
        else if (engine!.RemovedModel is { } removed)
            ModelNotice.Show("Your speech model is missing", $"{removed} is no longer on this PC. Download it again, or choose another model, to dictate.", "Choose a model");
        else
            ModelNotice.Show("Download a speech model to start",
                "Speak Forever needs a speech model to understand you. Turbo is recommended: a 574 MB download that uses about 1 GB of memory.", "Choose a model");
    }

    void ModelNotice_ActionClick(object sender, RoutedEventArgs e) => Tabs.SelectedItem = Tabs.Items[SpeechModelTab];

    // ---- Row actions (the buttons in the model list's template) ------------------------------

    static ModelRow RowOf(object sender) => (ModelRow)((FrameworkElement)sender).DataContext;

    async void UseModel_Click(object sender, RoutedEventArgs e) => await engine!.LoadModelAsync(RowOf(sender).Path); // logs its own failures

    async void DownloadModel_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender).CatalogEntry is { } model) await DownloadAsync(model);
    }

    async void DeleteModel_Click(object sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        await DeleteModelAsync(row.Name, row.Path);
    }

    void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        if (downloads.TryGetValue(RowOf(sender).Path, out var cancel)) cancel.Cancel();
    }

    /// <summary>Downloads a model with progress in its row; the first model downloaded is loaded straight away.</summary>
    async Task DownloadAsync(ModelInfo model)
    {
        using var cancel = new CancellationTokenSource();
        downloads[model.LocalPath] = cancel;
        RefreshModels();
        var row = modelRows.First(r => string.Equals(r.Path, model.LocalPath, StringComparison.OrdinalIgnoreCase));
        row.Progress = 0;
        // Created on the UI thread, so reports arrive here; the downloader sends at most 101 of them.
        var progress = new Progress<double>(p => row.Progress = p);
        try
        {
            await ModelCatalog.DownloadAsync(model, progress, cancel.Token);
            if (engine!.LoadedModel is null && !engine.IsLoadingModel)
                _ = engine.LoadModelAsync(model.LocalPath); // logs its own failures
        }
        catch (OperationCanceledException)
        {
            Log.Info($"Cancelled the {model.Name} download.");
        }
        catch (Exception e)
        {
            Log.Warn($"Couldn't download {model.Name}: {e.Message}");
        }
        finally
        {
            downloads.Remove(model.LocalPath);
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
        // Dragging fires ValueChanged dozens of times; apply and save once it settles.
        pauseSave = DispatcherQueue.CreateTimer();
        pauseSave.Interval = SaveDelay;
        pauseSave.IsRepeating = false;
        pauseSave.Tick += async (_, _) => await SavePauseAsync();
    }

    void PauseSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        PauseText.Text = $"{e.NewValue:F1} s";
        pendingSilenceMs = (int)Math.Round(e.NewValue * 1000);
        pauseSave?.Start(); // restarts the delay if it's already pending
    }

    /// <summary>Applies a slider change the user has finished making; also run on close, so none is lost.</summary>
    async Task SavePauseAsync()
    {
        pauseSave?.Stop();
        if (pendingSilenceMs is not { } ms || engine is null) return;
        pendingSilenceMs = null;
        try
        {
            await engine.UpdateConfigAsync(c => c with { SilenceMs = ms });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            Log.Warn($"Couldn't save settings: {e.Message}");
        }
    }

    // ---- Microphone test --------------------------------------------------------------------

    /// <summary>
    /// Runs the test in a dialog, so its result shows on this tab: listening, then transcribing,
    /// then what was heard. Closing the dialog early cancels the test.
    /// </summary>
    async void TestMicButton_Click(object sender, RoutedEventArgs e)
    {
        if (engine is null || testingMic) return;
        testingMic = true;
        UpdateState();
        using var finish = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();
        var heard = new TextBlock { Text = "Listening… Say something, then pause or press Finish.", TextWrapping = TextWrapping.Wrap };
        var meta = new TextBlock { Style = (Style)Application.Current.Resources["Caption"], TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Microphone test",
            Content = new StackPanel { Spacing = 8, MinWidth = 360, Children = { heard, meta } },
            PrimaryButtonText = "Finish",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true; // stays open for the result
            finish.Cancel();
        };
        dialog.Closing += (_, _) => stop.Cancel();
        void OnPhase(DictationPhase phase) => DispatcherQueue.TryEnqueue(() =>
        {
            if (phase != DictationPhase.Transcribing || stop.IsCancellationRequested) return;
            heard.Text = "Transcribing…";
            dialog.PrimaryButtonText = "";
        });
        engine.PhaseChanged += OnPhase;
        var shown = dialog.ShowAsync();
        try
        {
            var result = await engine.TestMicAsync(finish.Token, stop.Token);
            if (result is var (text, took, seconds))
            {
                ShowHeard(text, took, seconds);
                heard.Text = LastHeardText.Text;
                meta.Text = LastHeardMeta.Text;
                Log.Info($"Microphone test: {seconds:F1} s of speech transcribed in {took.TotalMilliseconds:F0} ms: \"{text}\"");
            }
            else
            {
                heard.Text = $"Didn't hear any speech in {engine.Config.NoSpeechTimeoutSeconds} seconds. Check your microphone is connected and not muted.";
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The dialog was closed before the test finished.
        }
        catch (Exception ex)
        {
            heard.Text = $"The microphone test failed: {ex.Message}";
            Log.Warn(heard.Text);
        }
        finally
        {
            engine.PhaseChanged -= OnPhase;
        }
        dialog.PrimaryButtonText = "";
        dialog.CloseButtonText = "Close";
        dialog.DefaultButton = ContentDialogButton.Close;
        await shown;
        testingMic = false;
        UpdateState();
    }
}

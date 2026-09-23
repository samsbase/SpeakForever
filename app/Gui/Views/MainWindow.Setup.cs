using System.Security;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using SpeakForever.Logging;
using SpeakForever.Speech;
using SpeakForever.Updates;

namespace SpeakForever.Gui.Views;

/// <summary>
/// First run, over the whole window: find the game, get a voice model, test the microphone.
/// It shows while there's no voice model on this PC, until the user starts playing or skips it.
/// </summary>
public sealed partial class MainWindow
{
    // The installer's name for the sign-in entry too, so its uninstaller removes the app's own.
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunValue = "Speak Forever";
    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    bool inSetup;
    string? heardInSetup; // what the microphone test heard, which finishes the last step

    void ShowSetupIfNeeded()
    {
        inSetup = ModelCatalog.Installed().Count == 0;
        if (!inSetup) return;
        SetupPage.Visibility = Visibility.Visible;
        TabsBar.Visibility = DictationPage.Visibility = Visibility.Collapsed;
        StartupCheck.Visibility = UpdateChecker.CanInstallHere ? Visibility.Visible : Visibility.Collapsed;
        StartupCheck.IsChecked = StartsWithWindows();
    }

    void UpdateSetup()
    {
        if (!inSetup || engine is null) return;
        bool gameDone = engine.GameFound, modelDone = engine.LoadedModel is not null, heard = heardInSetup is not null;
        bool downloading = downloads.ContainsKey(Recommended.LocalPath);
        int current = !gameDone ? 0 : !modelDone ? 1 : !heard ? 2 : 3;
        ShowStep(0, SetupGameStep, SetupGameBadge, SetupGameBadgeText, current, gameDone);
        ShowStep(1, SetupModelStep, SetupModelBadge, SetupModelBadgeText, current, modelDone);
        ShowStep(2, SetupMicStep, SetupMicBadge, SetupMicBadgeText, current, heard);

        SetupGameTitle.Text = gameDone ? "Found WoW: Forever" : gameChecked ? "Where's WoW: Forever?" : "Looking for WoW: Forever…";
        SetupGameText.Text = gameDone ? engine.Config.GameFolder
            : gameChecked ? @"It isn't where Battle.net usually puts it. Choose the game's folder: for the beta, World of Warcraft\_classic_beta_." : "";
        SetupGameButton.Content = gameDone ? "Change" : "Choose folder";
        SetupGameButton.Style = ButtonStyle(gameDone ? "QuietButton" : "AccentButton");
        SetupGameButton.Visibility = gameChecked ? Visibility.Visible : Visibility.Collapsed;

        SetupModelTitle.Text = modelDone ? "Your voice model is ready"
            : downloading ? "Getting your voice model"
            : engine.IsLoadingModel ? "Loading your voice model"
            : "Get your voice model";
        SetupModelText.Text = modelDone ? engine.ModelStatus
            : $"{Recommended.Name}, our pick: a {ModelInfo.FormatBytes(Recommended.DownloadBytes)} download. It runs on this PC, so nothing you say is uploaded.";
        SetupModelButton.Visibility = modelDone || downloading || engine.IsLoadingModel ? Visibility.Collapsed : Visibility.Visible;
        SetupModelProgress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        if (downloading && modelRows.FirstOrDefault(r => r.Path == Recommended.LocalPath) is { } row)
        {
            SetupProgressBar.Value = row.Progress;
            SetupProgressText.Text = $"{row.Progress * Recommended.DownloadBytes / 1e6:F0} of {ModelInfo.FormatBytes(Recommended.DownloadBytes)}";
        }

        SetupMicText.Text = heardInSetup is { } text ? $"Heard you: \"{text}\""
            : modelDone ? "Say something to check your microphone."
            : "A quick microphone test, once the model is ready.";
        SetupMicButton.IsEnabled = modelDone && !testingMic;
        SetupMicButton.Style = ButtonStyle(modelDone && !heard ? "AccentButton" : "PanelButton");

        // One accent button at a time: the next step's, then this once everything's done.
        SetupDoneButton.Content = modelDone ? "Start playing" : "Skip for now";
        SetupDoneButton.Style = ButtonStyle(!modelDone ? "QuietButton" : heard ? "AccentButton" : "PanelButton");
    }

    /// <summary>Done steps get a tick, the current one the ornate frame, the rest wait their turn.</summary>
    static void ShowStep(int step, ContentControl card, Border badge, TextBlock badgeText, int current, bool done)
    {
        card.Style = (Style)Application.Current.Resources[step == current ? "Frame" : "Card"];
        card.Padding = step == current ? new Thickness(11, 8, 11, 8) : new Thickness(16, 14, 16, 14); // the frame's borders make up the rest
        badge.Background = Brush(done ? "ToneReadyFillBrush" : step == current ? "ArcaneBrush" : "CardBrush");
        badge.BorderBrush = Brush(done ? "ToneReadyBrush" : step == current ? "ArcaneBrush" : "MutedBrush");
        badgeText.Foreground = Brush(done ? "ToneReadyBrush" : step == current ? "NightBrush" : "MutedBrush");
        if (done)
        {
            badgeText.FontFamily = IconFont;
            badgeText.Text = "\uE73E"; // a tick
        }
        else
        {
            badgeText.ClearValue(TextBlock.FontFamilyProperty);
            badgeText.Text = $"{step + 1}";
        }
    }

    static Style ButtonStyle(string key) => (Style)Application.Current.Resources[key];

    void SetupDoneButton_Click(object sender, RoutedEventArgs e) => EndSetup(tab: 0);

    void SetupOtherModel_Click(object sender, RoutedEventArgs e) => EndSetup(SpeechModelTab);

    void EndSetup(int tab)
    {
        inSetup = false;
        SetupPage.Visibility = Visibility.Collapsed;
        TabsBar.Visibility = Visibility.Visible;
        ShowTab(tab);
        UpdateState();
        Root.UpdateLayout();
        FitToSpeechModelTab();
    }

    // ---- Starting with Windows ---------------------------------------------------------------

    static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is not null;
    }

    /// <summary>Returns whether it worked; says why not in the log.</summary>
    static bool SetStartsWithWindows(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            Log.Warn($"Couldn't change starting with Windows: {e.Message}");
            return false;
        }
    }

    void StartupCheck_Click(object sender, RoutedEventArgs e)
    {
        bool on = StartupCheck.IsChecked == true;
        if (!SetStartsWithWindows(on)) StartupCheck.IsChecked = !on;
        updating = true;
        StartupSwitch.IsOn = StartsWithWindows(); // the same setting on the Settings tab
        updating = false;
    }

    void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        if (!SetStartsWithWindows(StartupSwitch.IsOn))
        {
            updating = true;
            StartupSwitch.IsOn = !StartupSwitch.IsOn;
            updating = false;
        }
    }
}

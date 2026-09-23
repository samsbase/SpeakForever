using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeakForever.Logging;
using SpeakForever.Updates;
using Windows.Storage.Pickers;
using Windows.System;

namespace SpeakForever.Gui.Views;

/// <summary>The Settings tab: where WoW: Forever is installed, and updates.</summary>
public sealed partial class MainWindow
{
    const int SettingsTab = 3;
    static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);
    static readonly TimeSpan FirstUpdateDelay = TimeSpan.FromSeconds(15); // after startup and the model load

    readonly UpdateChecker updateChecker = new();
    readonly CancellationTokenSource stopUpdates = new();
    UpdateInfo? update;
    bool gameChecked, checkingUpdates, installingUpdate;

    /// <summary>Once the window is up: find the game (asking if it can't), then start checking for updates.</summary>
    async Task StartupChecksAsync()
    {
        if (engine is null) return;
        VersionText.Text = $"Speak Forever {UpdateChecker.CurrentVersion.ToString(3)}";
        UpdateChecker.DeleteDownloads(); // the installer that updated this copy, if one did
        updating = true;
        AutoUpdateSwitch.IsOn = engine.Config.CheckForUpdates;
        updating = false;

        string? found = null;
        try
        {
            found = await engine.FindGameAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save the game's folder: {e.Message}");
        }
        gameChecked = true;
        UpdateState();
        if (found is null) await AskForGameAsync();
        _ = PollForUpdatesAsync(stopUpdates.Token);
    }

    void UpdateSettingsTab()
    {
        if (engine is null) return;
        GameFolderText.Text = engine.GameFound ? engine.Config.GameFolder : gameChecked ? "Not found" : "Looking...";
        if (gameChecked && !engine.GameFound)
            GameNotice.Show("Where's WoW: Forever?", "Speak Forever couldn't find the game, and only types into it. Tell it where the game is installed.", "Settings");
        else
            GameNotice.Show(null);
    }

    void GameNotice_ActionClick(object sender, RoutedEventArgs e) => Tabs.SelectedItem = Tabs.Items[SettingsTab];

    // ---- Where the game is ------------------------------------------------------------------

    /// <summary>The first-run prompt, when the game isn't where Battle.net usually puts it.</summary>
    async Task AskForGameAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Where's WoW: Forever?",
            Content = "Speak Forever only ever types into WoW: Forever, so it needs to know where the game is installed. "
                      + "It looked where Battle.net installs WoW and didn't find it.\n\n"
                      + "Choose the folder with the game in it. In the beta that's World of Warcraft\\_classic_beta_; "
                      + "choosing the World of Warcraft folder above it works too.",
            PrimaryButtonText = "Choose folder",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
        };
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ChooseGameFolderAsync();
        }
        catch (COMException)
        {
            // Another dialog is already open (WinUI allows one); the notice on the Dictation tab still asks.
        }
    }

    async void ChooseGameButton_Click(object sender, RoutedEventArgs e) => await ChooseGameFolderAsync();

    async Task ChooseGameFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        string? error;
        try
        {
            error = await engine!.SetGameFolderAsync(folder.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"Couldn't save it: {e.Message}";
        }
        ShowGameMessage(error ?? "Found it. Speak Forever will type into the game from here.", error is not null);
    }

    async void FindGameButton_Click(object sender, RoutedEventArgs e)
    {
        FindGameButton.IsEnabled = false;
        try
        {
            var found = await engine!.FindGameAsync(searchAgain: true);
            ShowGameMessage(found is null ? "It isn't in any of the usual places. Choose its folder instead." : "Found it.", found is null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowGameMessage($"Couldn't save it: {ex.Message}", warning: true);
        }
        finally
        {
            FindGameButton.IsEnabled = true;
        }
    }

    void ShowGameMessage(string text, bool warning)
    {
        GameMessage.Text = text;
        GameMessage.Foreground = warning ? warningBrush : normalBrush;
        GameMessage.Visibility = Visibility.Visible;
    }

    // ---- Updates ----------------------------------------------------------------------------

    /// <summary>
    /// Checks at launch (a little after, so the model loads first) and every few hours. The timer
    /// costs nothing between checks, and each check is one small request off the UI thread.
    /// </summary>
    async Task PollForUpdatesAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(FirstUpdateDelay, ct);
            using var timer = new PeriodicTimer(UpdateInterval);
            do
            {
                if (engine!.Config.CheckForUpdates) await CheckForUpdatesAsync(userAsked: false);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
    }

    async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(userAsked: true);

    async Task CheckForUpdatesAsync(bool userAsked)
    {
        if (checkingUpdates) return;
        checkingUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        if (userAsked) UpdateStatusText.Text = "Checking...";
        var before = update;
        try
        {
            update = await updateChecker.CheckAsync(stopUpdates.Token);
            UpdateStatusText.Text = update is { } found
                ? $"Speak Forever {found.Version.ToString(3)} is available."
                : $"No newer version found. Checked at {DateTime.Now:HH:mm}.";
            if (update is not null && update != before) Log.Info($"Speak Forever {update.Version.ToString(3)} is available: {update.PageUrl}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stopUpdates.IsCancellationRequested)
        {
            // Offline, or GitHub is busy: say so if asked, otherwise try again next time.
            if (userAsked) UpdateStatusText.Text = $"Couldn't reach GitHub: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            return; // the window is closing
        }
        finally
        {
            checkingUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
        }
        if (installingUpdate) return; // the notice is showing the download
        bool install = update?.Installer is not null && UpdateChecker.CanInstallHere;
        DownloadUpdateButton.Content = install ? "Update now" : "Download";
        DownloadUpdateButton.Visibility = update is null ? Visibility.Collapsed : Visibility.Visible;
        if (update is { } u)
            UpdateNotice.Show($"Speak Forever {u.Version.ToString(3)} is available",
                install ? "Update now downloads it, then Speak Forever closes, updates and opens again. Your settings and models stay."
                        : "Download the new installer from GitHub and run it; it updates this copy in place.",
                install ? "Update now" : "Download");
        else
            UpdateNotice.Show(null);
    }

    /// <summary>
    /// Downloads the installer and runs it over this copy, then closes so it can. Builds that
    /// weren't installed, and releases without an installer, open the release page instead.
    /// </summary>
    async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (update is not { Installer: not null } u || !UpdateChecker.CanInstallHere)
        {
            await Launcher.LaunchUriAsync(new Uri(update?.PageUrl ?? UpdateChecker.ReleasesUrl));
            return;
        }
        if (installingUpdate) return;
        installingUpdate = true;
        DownloadUpdateButton.IsEnabled = CheckUpdatesButton.IsEnabled = false;
        var title = $"Updating to Speak Forever {u.Version.ToString(3)}";
        UpdateNotice.Show(title, "Downloading...");
        var progress = new Progress<double>(p => UpdateNotice.Text = UpdateStatusText.Text = $"Downloading... {p:P0}");
        try
        {
            var installer = await UpdateChecker.DownloadInstallerAsync(u, progress, stopUpdates.Token);
            Log.Info($"Installing Speak Forever {u.Version.ToString(3)}; it opens again when done.");
            UpdateChecker.StartInstaller(installer);
            Close();
            return;
        }
        catch (OperationCanceledException) when (stopUpdates.IsCancellationRequested)
        {
            return; // the window is closing
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                      or InvalidDataException or TimeoutException or Win32Exception)
        {
            Log.Warn($"Couldn't update: {ex.Message}");
            UpdateNotice.Show(title, $"Couldn't update: {ex.Message}", "Try again");
            UpdateStatusText.Text = $"Couldn't update: {ex.Message}";
        }
        installingUpdate = false;
        DownloadUpdateButton.IsEnabled = CheckUpdatesButton.IsEnabled = true;
    }

    async void AutoUpdateSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (updating || engine is null) return;
        bool on = AutoUpdateSwitch.IsOn;
        try
        {
            await engine.UpdateConfigAsync(c => c with { CheckForUpdates = on });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save settings: {ex.Message}");
        }
        if (on) await CheckForUpdatesAsync(userAsked: false);
    }
}

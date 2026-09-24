using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeakForever.Logging;
using SpeakForever.Speech;
using SpeakForever.Updates;
using Windows.System;

namespace SpeakForever.Gui.Views;

/// <summary>The Settings tab: where WoW: Forever is installed, the microphone, updates, and starting with Windows.</summary>
public sealed partial class MainWindow
{
    const int SettingsTab = 3;
    static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);
    static readonly TimeSpan FirstUpdateDelay = TimeSpan.FromSeconds(15); // after startup and the model load

    readonly UpdateChecker updateChecker = new();
    readonly CancellationTokenSource stopUpdates = new();
    UpdateInfo? update;
    bool checkingUpdates, installingUpdate, micFound = true;

    /// <summary>The tab's contents, before the window is sized to fit them: the startup card is only for an installed copy.</summary>
    void InitializeSettingsTab()
    {
        VersionText.Text = $"Speak Forever {UpdateChecker.CurrentVersion.ToString(3)}";
        updating = true;
        AutoUpdateSwitch.IsOn = engine!.Config.CheckForUpdates;
        OverlaySwitch.IsOn = engine.Config.ShowOverlay;
        StartupCard.Visibility = UpdateChecker.CanInstallHere ? Visibility.Visible : Visibility.Collapsed;
        StartupSwitch.IsOn = StartsWithWindows();
        updating = false;
    }

    /// <summary>Once the window is up: start checking for updates.</summary>
    void StartupChecks()
    {
        if (engine is null) return;
        UpdateChecker.DeleteDownloads(); // the installer that updated this copy, if one did
        _ = PollForUpdatesAsync(stopUpdates.Token);
    }

    // ---- Microphone -------------------------------------------------------------------------

    /// <summary>
    /// Lists the microphones again: at startup, when the window comes back to the front and when the
    /// list opens, so one plugged in meanwhile shows up. WinMM has no event for it.
    /// </summary>
    void RefreshMics()
    {
        if (engine is null) return;
        var names = Microphones.Names();
        micFound = names.Count > 0;
        string[] items = micFound ? ["Default", .. names] : []; // empty shows "None found"
        int selected = micFound ? Microphones.Resolve(engine.Config.MicDevice) + 1 : -1;
        updating = true;
        foreach (var combo in (ComboBox[])[MicCombo, HomeMicCombo])
        {
            // Only when it changed: replacing the list under an open dropdown would close it.
            if (combo.ItemsSource is not string[] shown || !shown.SequenceEqual(items)) combo.ItemsSource = items;
            combo.SelectedIndex = selected;
            combo.IsEnabled = micFound;
        }
        updating = false;
        UpdateState();
    }

    void UpdateMic()
    {
        MicMessage.Text = Microphones.NoneFound;
        MicMessage.Visibility = micFound ? Visibility.Collapsed : Visibility.Visible;
        MicNotice.Show(micFound ? null : "No microphone found", "Plug in a microphone or headset, or turn yours on in Windows' sound settings.", "Sound settings");
    }

    void MicCombo_DropDownOpened(object sender, object e) => RefreshMics();

    /// <summary>The Home tab's and the Settings tab's lists are the same setting.</summary>
    async void MicCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || engine is null || sender is not ComboBox { SelectedIndex: >= 0 } combo) return;
        int device = combo.SelectedIndex - 1;
        updating = true;
        MicCombo.SelectedIndex = HomeMicCombo.SelectedIndex = combo.SelectedIndex;
        updating = false;
        Log.Info($"Microphone: {combo.SelectedItem}.");
        try
        {
            await engine.UpdateConfigAsync(c => c with { MicDevice = device });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save settings: {ex.Message}");
        }
        UpdateState();
    }

    async void SoundSettings_Click(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri("ms-settings:sound"));

    async void OverlaySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (updating || engine is null) return;
        bool on = OverlaySwitch.IsOn;
        try
        {
            await engine.UpdateConfigAsync(c => c with { ShowOverlay = on });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save settings: {ex.Message}");
        }
        UpdateOverlay();
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
        if (userAsked) UpdateStatusText.Text = "Checking…";
        var before = update;
        try
        {
            update = await updateChecker.CheckAsync(stopUpdates.Token);
            UpdateStatusText.Text = update is { } found
                ? $"Speak Forever {found.Version.ToString(3)} is available."
                : $"You have the latest version. Checked at {DateTime.Now:HH:mm}.";
            if (update is not null && update != before) Log.Info($"Speak Forever {update.Version.ToString(3)} is available: {update.PageUrl}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stopUpdates.IsCancellationRequested)
        {
            // Offline, or GitHub is busy: say so if asked, otherwise try again next time.
            if (userAsked) UpdateStatusText.Text = $"Couldn't check for updates: {ex.Message}";
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
                install ? "Speak Forever will download it, close, update and reopen. Your settings and models are kept."
                        : "Download the installer from GitHub and run it to update. Your settings and models are kept.",
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
        UpdateNotice.Show(title, "Downloading…");
        var progress = new Progress<double>(p => UpdateNotice.Text = UpdateStatusText.Text = $"Downloading… {p:P0}");
        try
        {
            var installer = await UpdateChecker.DownloadInstallerAsync(u, progress, stopUpdates.Token);
            Log.Info($"Installing Speak Forever {u.Version.ToString(3)}. It will reopen when it's done.");
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

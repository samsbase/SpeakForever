using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeakForever.Input;
using Windows.System;
using Windows.UI.Core;

namespace SpeakForever.Gui.Views;

/// <summary>The Buttons &amp; shortcuts tab: rebinding the controller buttons and the keyboard shortcut.</summary>
public sealed partial class MainWindow
{
    static readonly TimeSpan ChordTimeout = TimeSpan.FromSeconds(10);

    Recording recording;
    CancellationTokenSource? rebindCancel;

    /// <summary>Which binding the user is recording, if any.</summary>
    enum Recording
    {
        None,
        OpenChat,
        Dictate,
        Keyboard,
    }

    void UpdateButtonsTab()
    {
        var cfg = engine!.Config;
        // Recording a controller combo needs the controller loop; the button in use doubles as Cancel.
        bool canRebind = engine.IsRunning && engine.ControllerSlot >= 0;
        OpenChatButton.IsEnabled = recording == Recording.OpenChat || (canRebind && recording == Recording.None);
        DictateButton.IsEnabled = recording == Recording.Dictate || (canRebind && recording == Recording.None);
        KeyboardButton.IsEnabled = recording is Recording.Keyboard or Recording.None;
        KeyboardOffButton.Visibility = cfg.KeyboardShortcut is null || recording == Recording.Keyboard ? Visibility.Collapsed : Visibility.Visible;
        KeyboardCaption.Foreground = engine.KeyboardError is null ? Brush("SilverBrush") : warningBrush;
        KeyboardCaption.Text = engine.KeyboardError
            ?? "Optional. Types into whichever text box you're in, in any program, like Windows+H. Press it again to finish early.";
    }

    void ShowBindings()
    {
        var cfg = engine!.Config;
        OpenChatText.Text = cfg.OpenChatChord;
        DictateText.Text = cfg.DictateChord;
        KeyboardText.Text = cfg.KeyboardShortcut ?? "Off";
        PauseHint.Text = $"Or press {cfg.DictateChord} again to finish straight away.";
        if (!heardAnything)
            LastHeardText.Text = $"Nothing yet. In the game, open chat with {cfg.OpenChatChord} and press {cfg.DictateChord}. Or try Test microphone on the Speech model tab.";
    }

    void ShowBindingMessage(string text, bool warning)
    {
        BindingMessage.Text = text;
        BindingMessage.Foreground = warning ? warningBrush : normalBrush;
        BindingMessage.Visibility = Visibility.Visible;
    }

    // ---- Controller bindings --------------------------------------------------------------

    async void OpenChatButton_Click(object sender, RoutedEventArgs e) =>
        await RebindControllerAsync(Recording.OpenChat, BindingKind.OpenChat, OpenChatButton, OpenChatText);

    async void DictateButton_Click(object sender, RoutedEventArgs e) =>
        await RebindControllerAsync(Recording.Dictate, BindingKind.Dictate, DictateButton, DictateText);

    async Task RebindControllerAsync(Recording which, BindingKind kind, Button button, TextBlock value)
    {
        if (engine is null) return;
        if (recording != Recording.None)
        {
            rebindCancel?.Cancel();
            return;
        }
        recording = which;
        rebindCancel = new CancellationTokenSource();
        button.Content = "Cancel";
        value.Text = "Press…";
        ShowBindingMessage("Hold any extra buttons, press the main one, then let go of them all.", warning: false);
        UpdateState();
        try
        {
            var chord = await engine.CaptureChordAsync(ChordTimeout, rebindCancel.Token);
            if (chord is null)
                ShowBindingMessage("No change.", warning: false);
            else if (await engine.SetBindingAsync(kind, chord.Value) is { } error)
                ShowBindingMessage(error + " No change.", warning: true);
            else
                ShowBindingMessage(kind == BindingKind.OpenChat
                    ? $"Open chat is now {chord.Value.Text}. Make sure it matches the game's binding."
                    : $"Dictate is now {chord.Value.Text}.", warning: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ShowBindingMessage(ex.Message, warning: true);
        }
        finally
        {
            recording = Recording.None;
            rebindCancel.Dispose();
            rebindCancel = null;
            button.Content = "Change";
            ShowBindings();
            UpdateState();
        }
    }

    // ---- Keyboard shortcut ----------------------------------------------------------------

    void KeyboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (engine is null) return;
        if (recording == Recording.Keyboard)
        {
            EndKeyboardRecording("No change.", warning: false, changed: false);
            return;
        }
        recording = Recording.Keyboard;
        engine.SuspendKeyboardShortcut(); // the old shortcut mustn't fire while recording a new one
        KeyboardButton.Content = "Cancel";
        KeyboardText.Text = "Press keys…";
        ShowBindingMessage("Hold Ctrl, Alt or Shift and press a key, or press an F key on its own. Esc cancels.", warning: false);
        ButtonsPage.Focus(FocusState.Programmatic); // so Space doesn't also press the button
        UpdateState();
    }

    async void KeyboardOffButton_Click(object sender, RoutedEventArgs e)
    {
        if (engine is null) return;
        if (await SetShortcutAsync(null) is { } error) ShowBindingMessage(error, warning: true);
        else ShowBindingMessage("Keyboard shortcut is off.", warning: false);
        ShowBindings();
        UpdateState();
    }

    async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (recording != Recording.Keyboard || engine is null) return;
        e.Handled = true; // before any await: the key mustn't reach the focused control
        var key = e.Key;
        if (key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftMenu or VirtualKey.RightMenu)
            return; // wait for the actual key
        if (key == VirtualKey.Escape)
        {
            EndKeyboardRecording("No change.", warning: false, changed: false);
            return;
        }
        uint modifiers = 0;
        if (Down(VirtualKey.Control)) modifiers |= Shortcut.Ctrl;
        if (Down(VirtualKey.Menu)) modifiers |= Shortcut.Alt;
        if (Down(VirtualKey.Shift)) modifiers |= Shortcut.Shift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= Shortcut.Win;
        var shortcut = new Shortcut(modifiers, (uint)key);
        if (Shortcut.NameOf(shortcut.Key) is null)
        {
            ShowBindingMessage("That key can't be used. Try another.", warning: true);
            return;
        }
        if (await SetShortcutAsync(shortcut) is { } error)
            EndKeyboardRecording(error + " No change.", warning: true, changed: false);
        else
            EndKeyboardRecording($"Keyboard shortcut is now {shortcut}.", warning: false, changed: true);

        static bool Down(VirtualKey k) => InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down);
    }

    /// <summary>Returns why the shortcut couldn't be set (taken, or the settings couldn't be saved), or null.</summary>
    async Task<string?> SetShortcutAsync(Shortcut? shortcut)
    {
        try
        {
            return await engine!.SetKeyboardShortcutAsync(shortcut);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't save settings: {e.Message}";
        }
    }

    void EndKeyboardRecording(string message, bool warning, bool changed)
    {
        recording = Recording.None;
        if (!changed) engine?.ResumeKeyboardShortcut();
        KeyboardButton.Content = "Change";
        ShowBindingMessage(message, warning);
        ShowBindings();
        UpdateState();
    }
}

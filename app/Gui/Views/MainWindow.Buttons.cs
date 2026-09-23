using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeakForever.Gui.Controls;
using SpeakForever.Input;
using Windows.System;
using Windows.UI.Core;

namespace SpeakForever.Gui.Views;

/// <summary>The Controls tab: rebinding the controller buttons and the keyboard shortcut.</summary>
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
        Redo,
        Keyboard,
    }

    void UpdateButtonsTab()
    {
        var cfg = engine!.Config;
        // Recording a controller combo needs the controller loop; the button in use doubles as Cancel.
        bool canRebind = engine.IsRunning && engine.ControllerSlot >= 0;
        OpenChatButton.IsEnabled = recording == Recording.OpenChat || (canRebind && recording == Recording.None);
        DictateButton.IsEnabled = recording == Recording.Dictate || (canRebind && recording == Recording.None);
        RedoButton.IsEnabled = recording == Recording.Redo || (canRebind && recording == Recording.None);
        KeyboardButton.IsEnabled = recording is Recording.Keyboard or Recording.None;
        KeyboardOffButton.Visibility = cfg.KeyboardShortcut is null || recording == Recording.Keyboard ? Visibility.Collapsed : Visibility.Visible;
        RebindHint.Text = !engine.IsRunning ? "Turn on Active (on the Home tab) to change the controller buttons."
                        : "Connect a controller to change its buttons.";
        RebindHint.Visibility = canRebind || recording != Recording.None ? Visibility.Collapsed : Visibility.Visible;
        KeyboardCaption.Foreground = engine.KeyboardError is null ? Brush("MutedBrush") : warningBrush;
        KeyboardCaption.Text = engine.KeyboardError
            ?? "Optional. Types into whichever text box you're in, in any program, like Windows+H. Press it again to finish early.";
    }

    /// <summary>The bindings, drawn as the connected controller's own button icons.</summary>
    void ShowBindings()
    {
        var cfg = engine!.Config;
        var style = engine.ButtonStyle;
        shownStyle = style;
        Chord openChat = Chord.Parse(cfg.OpenChatChord), dictate = Chord.Parse(cfg.DictateChord);
        if (recording != Recording.OpenChat) OpenChatCap.Child = ButtonPrompt.Icons(openChat, style);
        if (recording != Recording.Dictate) DictateCap.Child = ButtonPrompt.Icons(dictate, style);
        if (recording != Recording.Redo) RedoCap.Child = ButtonPrompt.Icons(Chord.Parse(cfg.RedoChord), style);
        KeyboardText.Text = cfg.KeyboardShortcut ?? "Off";
        ButtonPrompt.Fill(PauseHint, "Or press {0} again to finish straight away.", style, dictate);
        ButtonPrompt.Fill(LastHeardHint, "Nothing yet. What you say shows up here as well as in the game.");
    }

    /// <summary>A message under the bindings; "{0}" in it is drawn as the chord's button icons.</summary>
    void ShowBindingMessage(string text, bool warning, Chord? chord = null)
    {
        if (chord is { } c) ButtonPrompt.Fill(BindingMessage, text, engine!.ButtonStyle, c);
        else ButtonPrompt.Fill(BindingMessage, text);
        BindingMessage.Foreground = warning ? warningBrush : normalBrush;
        BindingMessage.Visibility = Visibility.Visible;
    }

    // ---- Controller bindings --------------------------------------------------------------

    async void OpenChatButton_Click(object sender, RoutedEventArgs e) =>
        await RebindControllerAsync(Recording.OpenChat, BindingKind.OpenChat, OpenChatButton, OpenChatCap);

    async void DictateButton_Click(object sender, RoutedEventArgs e) =>
        await RebindControllerAsync(Recording.Dictate, BindingKind.Dictate, DictateButton, DictateCap);

    async void RedoButton_Click(object sender, RoutedEventArgs e) =>
        await RebindControllerAsync(Recording.Redo, BindingKind.Redo, RedoButton, RedoCap);

    async Task RebindControllerAsync(Recording which, BindingKind kind, Button button, Border cap)
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
        cap.Child = new TextBlock { Text = "Press…", Style = (Style)Application.Current.Resources["KeycapText"], VerticalAlignment = VerticalAlignment.Center };
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
                ShowBindingMessage(kind switch
                {
                    BindingKind.OpenChat => "Open chat is now {0}. Make sure it matches the game's binding.",
                    BindingKind.Dictate => "Dictate is now {0}.",
                    _ => "Start over is now {0}.",
                }, warning: false, chord.Value);
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
        ShowBindingMessage("Press a key, or hold Ctrl, Alt or Shift and press one. Esc cancels.", warning: false);
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

    /// <summary>
    /// Controller buttons reach the window as keys too: A clicks the focused button (by then the
    /// Cancel button) and the D-pad moves focus. While a binding is being recorded, they're the
    /// recording's, not the window's.
    /// </summary>
    static bool FromController(KeyRoutedEventArgs e) =>
        e.OriginalKey is >= VirtualKey.GamepadA and <= VirtualKey.GamepadRightThumbstickLeft;

    void OnPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (recording != Recording.None && FromController(e)) e.Handled = true; // buttons click on release
    }

    async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (recording != Recording.None && FromController(e))
        {
            e.Handled = true;
            return;
        }
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
            ShowBindingMessage("That key can't be used for a shortcut. Try another.", warning: true);
            return;
        }
        if (await SetShortcutAsync(shortcut) is { } error)
            EndKeyboardRecording(error + " No change.", warning: true, changed: false);
        else
            EndKeyboardRecording($"Keyboard shortcut is now {shortcut}. While Speak Forever is active, Windows sends {shortcut} to it instead of the program you're in.",
                                 warning: false, changed: true);

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

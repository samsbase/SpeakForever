using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeakForever.Gui.Controls;
using SpeakForever.Input;

namespace SpeakForever.Gui.Views;

/// <summary>The in-game overlay: when it shows and what it says. The window itself is <see cref="OverlayWindow"/>.</summary>
public sealed partial class MainWindow
{
    const int MaxLeftOutShown = 60; // characters of the left-out words quoted in the overlay; the Home tab has them all
    static readonly TimeSpan TooLongShowsFor = TimeSpan.FromSeconds(6);

    OverlayWindow? overlay;
    DispatcherQueueTimer? tooLongTimer;
    string? tooLong; // what didn't fit in the chat box, while the overlay is saying so
    bool overlayClosed; // the app is closing: a late dictation event mustn't open a new overlay window

    /// <summary>Follows the dictation: listening, then typing, then gone, unless it's saying a message didn't fit.</summary>
    void UpdateOverlay()
    {
        if (engine is null || overlayClosed) return;
        if (!engine.Config.ShowOverlay)
        {
            overlay?.Hide();
            return;
        }
        if (phase == DictationPhase.Listening)
        {
            tooLong = null; // a new dictation: the warning has done its job
            Overlay().Show("Listening", FinishButton());
        }
        else if (phase == DictationPhase.Transcribing) Overlay().Show("Typing…", barSpeed: 2.5);
        else if (tooLong is not null) ShowTooLongOverlay();
        else overlay?.Hide();
    }

    /// <summary>The words that didn't fit in the chat box, on the Home tab and, for a few seconds, in the overlay.</summary>
    void ShowTooLong(string leftOut)
    {
        LastHeardTooLong.Text = $"Too long for WoW's chat box, so this was left out: \"{leftOut}\"";
        LastHeardTooLong.Visibility = Visibility.Visible;
        if (engine?.Config.ShowOverlay != true || overlayClosed) return;
        tooLong = leftOut;
        ShowTooLongOverlay();
        if (tooLongTimer is null)
        {
            tooLongTimer = DispatcherQueue.CreateTimer();
            tooLongTimer.IsRepeating = false;
            tooLongTimer.Tick += (_, _) =>
            {
                tooLong = null;
                UpdateOverlay();
            };
        }
        tooLongTimer.Interval = TooLongShowsFor;
        tooLongTimer.Start(); // restarts it if a warning is already showing
    }

    void ShowTooLongOverlay()
    {
        var quoted = tooLong!.Length <= MaxLeftOutShown ? tooLong : tooLong[..MaxLeftOutShown].TrimEnd() + "…";
        // Braces would read as a button in the template; Whisper doesn't write them, but a stray one mustn't break the line.
        var text = $"Left out: \"{quoted.Replace('{', '(').Replace('}', ')')}\"";
        var style = engine!.ButtonStyle;
        var redo = Chord.Parse(engine.Config.RedoChord);
        bool controller = engine.ControllerSlot >= 0;
        Overlay().Show("Too long for chat", warning: true, detail: target =>
        {
            if (controller) ButtonPrompt.Fill(target, text + " Press {0} to start over.", style, redo);
            else ButtonPrompt.Fill(target, text);
        });
    }

    /// <summary>What finishes the dictation early: the dictate button with a controller, otherwise the keyboard shortcut.</summary>
    FrameworkElement? FinishButton()
    {
        var cfg = engine!.Config;
        if (engine.ControllerSlot >= 0) return ButtonPrompt.Icons(Chord.Parse(cfg.DictateChord), engine.ButtonStyle, size: 32);
        if (cfg.KeyboardShortcut is not { } key) return null;
        return new Border
        {
            Background = Brush("NightBrush"), BorderBrush = Brush("IndigoBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 4), Margin = new Thickness(4, 0, 0, 0),
            Child = new TextBlock { Text = key, Style = (Style)Application.Current.Resources["KeycapText"], FontSize = 13 },
        };
    }

    /// <summary>Made the first time it's needed, so it costs nothing for anyone who turns it off.</summary>
    OverlayWindow Overlay() => overlay ??= new OverlayWindow();

    void CloseOverlay()
    {
        overlayClosed = true;
        tooLongTimer?.Stop();
        overlay?.Close();
        overlay = null;
    }
}

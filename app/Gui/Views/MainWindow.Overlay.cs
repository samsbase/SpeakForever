using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeakForever.Gui.Controls;
using SpeakForever.Input;

namespace SpeakForever.Gui.Views;

/// <summary>The in-game overlay: when it shows and what it says. The window itself is <see cref="OverlayWindow"/>.</summary>
public sealed partial class MainWindow
{
    const string PasteGlyph = "", WarningGlyph = "";

    OverlayWindow? overlay;
    bool tooLong; // what's ready to paste is only the start of what was said
    bool overlayClosed; // the app is closing: a late dictation event mustn't open a new overlay window

    /// <summary>Follows the dictation: listening, then transcribing, then ready to paste until it's sent, then gone.</summary>
    void UpdateOverlay()
    {
        if (engine is null || overlayClosed) return;
        if (!engine.Config.ShowOverlay)
        {
            overlay?.Hide();
            return;
        }
        bool controller = engine.ControllerSlot >= 0;
        if (phase == DictationPhase.Listening)
        {
            tooLong = false; // a new dictation: the warning has done its job
            Overlay().Show("Listening", FinishButton(controller), followVoice: true);
        }
        else if (phase == DictationPhase.Transcribing) Overlay().Show("Transcribing…", barSpeed: 2.5);
        else if (phase == DictationPhase.Ready)
            Overlay().Show(ReadyHeadline(tooLong), detail: ReadyDetail(controller, tooLong), glyph: tooLong ? WarningGlyph : PasteGlyph, warning: tooLong);
        else overlay?.Hide();
    }

    /// <summary>The words that didn't fit in the chat box, on the Home tab; the overlay says so while the rest waits to be pasted.</summary>
    void ShowTooLong(string leftOut)
    {
        LastHeardTooLong.Text = $"Too long for WoW's chat box, so only the start was copied. Left out: \"{leftOut}\"";
        LastHeardTooLong.Visibility = Visibility.Visible;
        tooLong = true;
    }

    static string ReadyHeadline(bool cutShort) => cutShort ? "Too long for chat" : "Ready to paste";

    /// <summary>How to paste it, and how to cancel: the dictate button with a controller, otherwise the keyboard shortcut.</summary>
    Action<RichTextBlock> ReadyDetail(bool controller, bool cutShort)
    {
        var style = engine!.ButtonStyle;
        var dictate = Chord.Parse(engine.Config.DictateChord);
        var key = engine.Config.KeyboardShortcut ?? "the shortcut";
        var start = cutShort ? "Only the start fits. " : "";
        return target =>
        {
            if (controller) ButtonPrompt.Fill(target, start + "Press Ctrl+V to paste it, or {0} to cancel.", style, dictate);
            else ButtonPrompt.Fill(target, $"{start}Press Ctrl+V to paste it, or {key} to cancel.");
        };
    }

    /// <summary>What finishes the dictation early: the dictate button with a controller, otherwise the keyboard shortcut.</summary>
    FrameworkElement? FinishButton(bool controller)
    {
        var cfg = engine!.Config;
        if (controller) return ButtonPrompt.Icons(Chord.Parse(cfg.DictateChord), engine.ButtonStyle, size: 32);
        if (cfg.KeyboardShortcut is not { } key) return null;
        return new Border
        {
            Background = Brush("NightBrush"), BorderBrush = Brush("IndigoBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 4), Margin = new Thickness(4, 0, 0, 0),
            Child = new TextBlock { Text = key, Style = (Style)Application.Current.Resources["KeycapText"], FontSize = 13 },
        };
    }

    /// <summary>Made the first time it's needed, so it costs nothing for anyone who turns it off.</summary>
    OverlayWindow Overlay()
    {
        if (overlay is not null) return overlay;
        overlay = new OverlayWindow();
        FitOverlay();
        return overlay;
    }

    /// <summary>Sizes the pill for its biggest messages with today's buttons, so every state fits the one pill.</summary>
    void FitOverlay() =>
        overlay!.FitTo(("Listening", FinishButton(controller: true), null, null),
                       ("Listening", FinishButton(controller: false), null, null),
                       (ReadyHeadline(true), null, ReadyDetail(controller: true, cutShort: true), WarningGlyph),
                       (ReadyHeadline(true), null, ReadyDetail(controller: false, cutShort: true), WarningGlyph));

    /// <summary>After a rebind or a different controller: the new icons may be wider than the pill.</summary>
    void RefitOverlay()
    {
        if (overlay is null) return;
        FitOverlay();
        UpdateOverlay(); // fitting filled the pill: back to what it should be saying
    }

    void CloseOverlay()
    {
        overlayClosed = true;
        overlay?.Close();
        overlay = null;
    }
}

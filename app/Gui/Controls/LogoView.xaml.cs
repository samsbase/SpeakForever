using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace VoiceForever.Gui.Controls;

/// <summary>The Voice Forever logo, which comes alive while a dictation is under way.</summary>
public sealed partial class LogoView : UserControl
{
    static readonly UISettings Settings = new();

    public LogoView() => InitializeComponent();

    /// <summary>
    /// Still when idle, so it costs nothing while you play. With Windows' animation effects turned
    /// off, a dictation shows still rings instead of ripples, so it still works as a status light.
    /// </summary>
    public void Show(DictationPhase phase)
    {
        Pulse.Stop();
        bool active = phase != DictationPhase.Idle;
        bool animate = Settings.AnimationsEnabled;
        ShowStill(active && !animate);
        if (!active || !animate) return;
        Pulse.SpeedRatio = phase == DictationPhase.Transcribing ? 2.5 : 1;
        Pulse.Begin();
    }

    void ShowStill(bool active)
    {
        Wave1.Opacity = active ? 0.7 : 0;
        Wave2.Opacity = active ? 0.4 : 0;
        Wave1Scale.ScaleX = Wave1Scale.ScaleY = active ? 0.55 : 0.28;
        Wave2Scale.ScaleX = Wave2Scale.ScaleY = active ? 0.85 : 0.28;
        Highlight.Opacity = active ? 1 : 0.7;
    }
}

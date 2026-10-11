using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace SpeakForever.Gui.Controls;

/// <summary>The Speak Forever logo, which comes alive while it listens and transcribes.</summary>
public sealed partial class LogoView : UserControl
{
    static readonly UISettings Settings = new();

    public LogoView() => InitializeComponent();

    /// <summary>
    /// Still when idle, so it costs nothing while you play. With Windows' animation effects turned
    /// off, a dictation lights the loop without pulsing, so it still works as a status light.
    /// </summary>
    public void Show(DictationPhase phase)
    {
        Pulse.Stop();
        bool active = phase is DictationPhase.Listening or DictationPhase.Transcribing;
        bool animate = Settings.AnimationsEnabled;
        Highlight.Opacity = active && !animate ? 1 : 0;
        if (!active || !animate) return;
        Pulse.SpeedRatio = phase == DictationPhase.Transcribing ? 2.5 : 1;
        Pulse.Begin();
    }
}

using Microsoft.UI.Xaml.Controls;

namespace VoiceForever.Gui.Controls;

/// <summary>The Voice Forever logo, which comes alive while a dictation is under way.</summary>
public sealed partial class LogoView : UserControl
{
    public LogoView() => InitializeComponent();

    /// <summary>Still when idle, so it costs nothing while you play.</summary>
    public void Show(DictationPhase phase)
    {
        Pulse.Stop();
        if (phase == DictationPhase.Idle) return;
        Pulse.SpeedRatio = phase == DictationPhase.Transcribing ? 2.5 : 1;
        Pulse.Begin();
    }
}

using Microsoft.UI.Xaml.Media;

namespace VoiceForever.Gui.Models;

/// <summary>One line of the Activity log, coloured by whether it's a warning.</summary>
public sealed class LogLine(string text, Brush brush)
{
    public string Text { get; } = text;
    public Brush Brush { get; } = brush;
}

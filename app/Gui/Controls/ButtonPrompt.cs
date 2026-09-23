using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SpeakForever.Input;
using SpeakForever.Presentation;

namespace SpeakForever.Gui.Controls;

/// <summary>Controller buttons drawn as the icons printed on the controller in use, not as "LB+RB+DOWN".</summary>
public static class ButtonPrompt
{
    /// <summary>A chord's icons side by side, named for screen readers ("L1 plus R1 plus D-pad down").</summary>
    public static FrameworkElement Icons(Chord chord, ButtonStyle style, double size = 34)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in ButtonIcons.Buttons(chord))
        {
            var file = Path.Combine(AppContext.BaseDirectory, "Assets", ButtonIcons.Folder, ButtonIcons.File(style, button));
            panel.Children.Add(new Image { Source = new SvgImageSource(new Uri(file)), Width = size, Height = size });
        }
        var spoken = ButtonIcons.Spoken(style, chord);
        AutomationProperties.SetName(panel, spoken);
        ToolTipService.SetToolTip(panel, spoken);
        return panel;
    }

    /// <summary>
    /// Fills a RichTextBlock with text where "{0}", "{1}"… are drawn as those chords' icons:
    /// "Chat open · press {0} to dictate".
    /// </summary>
    public static void Fill(RichTextBlock target, string template, ButtonStyle style, params Chord[] chords)
    {
        var paragraph = new Paragraph();
        int at = 0;
        while (at < template.Length)
        {
            int open = template.IndexOf('{', at);
            int close = open < 0 ? -1 : template.IndexOf('}', open);
            if (open < 0 || close < 0 || !int.TryParse(template.AsSpan(open + 1, close - open - 1), out int index) || index >= chords.Length)
            {
                paragraph.Inlines.Add(new Run { Text = template[at..] });
                break;
            }
            if (open > at) paragraph.Inlines.Add(new Run { Text = template[at..open] });
            var icons = Icons(chords[index], style, size: target.FontSize * 2);
            icons.Margin = new Thickness(0, -4, 0, -9); // sits on the text's baseline rather than above it
            paragraph.Inlines.Add(new InlineUIContainer { Child = icons });
            at = close + 1;
        }
        target.Blocks.Clear();
        target.Blocks.Add(paragraph);
    }

    /// <summary>Plain text in a RichTextBlock, in a colour.</summary>
    public static void Fill(RichTextBlock target, string text, Brush? foreground = null)
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = text });
        target.Blocks.Clear();
        target.Blocks.Add(paragraph);
        if (foreground is not null) target.Foreground = foreground;
    }
}

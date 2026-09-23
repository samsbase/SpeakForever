using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SpeakForever.Gui.Controls;

/// <summary>A banner with an icon, a title, a line of explanation and one action button.</summary>
public sealed partial class Notice : UserControl
{
    public Notice() => InitializeComponent();

    /// <summary>Raised when the action button is pressed.</summary>
    public event RoutedEventHandler? ActionClick;

    /// <summary>A Segoe Fluent Icons character, such as U+E896 (download).</summary>
    public string Glyph
    {
        get => IconGlyph.Glyph;
        set => IconGlyph.Glyph = value;
    }

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public string Text
    {
        get => BodyText.Text;
        set => BodyText.Text = value;
    }

    /// <summary>The button's text; empty hides the button.</summary>
    public string ActionLabel
    {
        get => ActionButton.Content as string ?? "";
        set
        {
            ActionButton.Content = value;
            ActionButton.Visibility = value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Shows the notice with this content, or with null hides it.</summary>
    public void Show(string? title, string text = "", string actionLabel = "")
    {
        Visibility = title is null ? Visibility.Collapsed : Visibility.Visible;
        if (title is null) return;
        Title = title;
        Text = text;
        ActionLabel = actionLabel;
    }

    void ActionButton_Click(object sender, RoutedEventArgs e) => ActionClick?.Invoke(this, e);
}

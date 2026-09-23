namespace SpeakForever.Dictation;

/// <summary>WoW's chat box, which holds 255 characters however many dictations go into it.</summary>
public static class ChatBox
{
    public const int MaxLength = 255;

    /// <summary>
    /// Splits text into what fits in the room left, cut after the last whole word, and what doesn't.
    /// Only a first message is ever cut inside a word, and only if it's one enormous word.
    /// </summary>
    /// <param name="used">Characters already in the chat box.</param>
    public static (string Fits, string LeftOut) Fit(string text, int used = 0)
    {
        int room = MaxLength - used;
        if (text.Length <= room) return (text, "");
        int cut = room <= 0 ? 0 : text.LastIndexOf(' ', room);
        if (cut <= 0) cut = used == 0 ? room : 0;
        return (text[..cut].TrimEnd(), text[cut..].Trim());
    }
}

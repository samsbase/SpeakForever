namespace SpeakForever;

/// <summary>Where a dictation is up to; the app's logo animates to match.</summary>
public enum DictationPhase
{
    Idle,
    Listening,
    Transcribing,

    /// <summary>The text is on the clipboard, waiting to be pasted into chat.</summary>
    Ready,
}

using VoiceForever.Configuration;

namespace VoiceForever.Dictation;

/// <summary>Audio cues, since you're looking at the game, not this window.</summary>
public static class Cue
{
    public static void Start(Config cfg) => Play(cfg, (880, 70));
    public static void Heard(Config cfg) => Play(cfg, (660, 50), (990, 50));
    public static void Cancel(Config cfg) => Play(cfg, (440, 50));
    public static void Error(Config cfg) => Play(cfg, (220, 220));

    /// <summary>Console.Beep blocks for the tone's length, so it plays on the thread pool, not the caller's thread.</summary>
    static void Play(Config cfg, params (int Hz, int Ms)[] tones)
    {
        if (!cfg.Sounds) return;
        _ = Task.Run(() =>
        {
            foreach (var (hz, ms) in tones) Console.Beep(hz, ms);
        });
    }
}

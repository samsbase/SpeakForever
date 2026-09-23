using System.Collections.Concurrent;
using SpeakForever.Configuration;

namespace SpeakForever.Dictation;

/// <summary>Audio cues, since you're looking at the game, not this window.</summary>
public static class Cue
{
    const int MaxQueued = 3;

    // One player thread: cues play in order instead of over each other, and only this thread waits
    // on Console.Beep (which blocks for the tone's length). A burst beyond a few cues is dropped.
    static readonly BlockingCollection<(int Hz, int Ms)[]> Queue = new(MaxQueued);
    static readonly Thread Player = StartPlayer();

    public static void Start(Config cfg) => Play(cfg, (880, 70));
    public static void Heard(Config cfg) => Play(cfg, (660, 50), (990, 50));
    public static void Cancel(Config cfg) => Play(cfg, (440, 50));
    public static void Error(Config cfg) => Play(cfg, (220, 220));

    static void Play(Config cfg, params (int Hz, int Ms)[] tones)
    {
        if (cfg.Sounds && Player.IsAlive) Queue.TryAdd(tones);
    }

    static Thread StartPlayer()
    {
        var thread = new Thread(() =>
        {
            foreach (var tones in Queue.GetConsumingEnumerable())
                foreach (var (hz, ms) in tones) Console.Beep(hz, ms);
        })
        { IsBackground = true, Name = "Cues" };
        thread.Start();
        return thread;
    }
}

using NAudio.Wave;

namespace SpeakForever.Speech;

/// <summary>The microphones Windows has, numbered as <see cref="Configuration.Config.MicDevice"/> counts them.</summary>
public static class Microphones
{
    public const string NoneFound = "No microphone found. Plug one in, or turn yours on in Windows' sound settings.";

    public static IReadOnlyList<string> Names() =>
        [.. Enumerable.Range(0, WaveIn.DeviceCount).Select(i => WaveIn.GetCapabilities(i).ProductName)];

    /// <summary>The one to record from: the chosen one while it's there, otherwise Windows' default (-1).</summary>
    // ponytail: by number, which shifts when another mic is unplugged; save the name instead if people swap mics a lot.
    public static int Resolve(int chosen) => chosen < WaveIn.DeviceCount ? chosen : -1;
}

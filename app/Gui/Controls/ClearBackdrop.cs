using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SpeakForever.Logging;

namespace SpeakForever.Gui.Controls;

/// <summary>
/// A see-through window backdrop: wherever the content draws nothing, what's behind the window
/// shows. The overlay uses it so the game shows around its pill's rounded ends.
/// </summary>
public sealed partial class ClearBackdrop : SystemBackdrop
{
    const int DQTYPE_THREAD_CURRENT = 2, DQTAT_COM_STA = 2;

    static Windows.UI.Composition.Compositor? compositor;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        try
        {
            compositor ??= CreateCompositor();
            connectedTarget.SystemBackdrop = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
        catch (Exception e)
        {
            // Only looks: the window keeps its plain background, so the overlay's pill gets square corners.
            Log.Warn($"Couldn't make the overlay see-through: {e.Message}");
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }

    /// <summary>
    /// Backdrops are drawn by Windows' own compositor, which needs a Windows.System dispatcher queue
    /// on this thread; a WinUI thread only has its own kind, so this adds one for the app's lifetime.
    /// </summary>
    static Windows.UI.Composition.Compositor CreateCompositor()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
        {
            var options = new DispatcherQueueOptions
            {
                Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = DQTYPE_THREAD_CURRENT, ApartmentType = DQTAT_COM_STA,
            };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _)); // never released: it lives as long as the thread
        }
        return new Windows.UI.Composition.Compositor();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DispatcherQueueOptions
    {
        public int Size, ThreadType, ApartmentType;
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
}

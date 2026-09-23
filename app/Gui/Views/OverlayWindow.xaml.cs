using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpeakForever.Gui.Controls;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace SpeakForever.Gui.Views;

/// <summary>
/// The in-game overlay: a pill near the top of the game's screen saying it's listening, like the
/// picture on the website. It floats over the game without ever taking focus from it, because a
/// dictation types into whichever window has focus, and only if that's the game. Nothing can draw
/// over exclusive fullscreen, so it needs WoW in Windowed (Fullscreen) or windowed mode.
/// </summary>
public sealed partial class OverlayWindow : Window
{
    const double FromTop = 0.07; // of the screen's height: under WoW's minimap buttons and zone text, clear of the chat
    const int GWL_EXSTYLE = -20;
    const nint WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    const int DWMWCP_DONOTROUND = 1;
    const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    static readonly UISettings Settings = new();

    readonly IntPtr hwnd;
    bool shown;

    public OverlayWindow()
    {
        InitializeComponent();
        SystemBackdrop = new ClearBackdrop();
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        AppWindow.IsShownInSwitchers = false;
        // Never activated, not even by a click, so the game keeps focus.
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        // Windows 11 rounds and outlines every window; the pill has its own. Windows 10 refuses both, and draws neither anyway.
        int corners = DWMWCP_DONOTROUND, border = unchecked((int)DWMWA_COLOR_NONE);
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    /// <summary>Shows the pill, or changes what it says if it's already up.</summary>
    /// <param name="headline">"Listening", say.</param>
    /// <param name="button">Icons for the button that finishes it, shown first; null for none.</param>
    /// <param name="detail">Fills a smaller line under the headline; null for none.</param>
    /// <param name="warning">In the warning colour, with a warning sign instead of the moving bars.</param>
    /// <param name="barSpeed">How fast the bars move: 1 while listening, faster while transcribing.</param>
    public void Show(string headline, FrameworkElement? button = null, Action<RichTextBlock>? detail = null, bool warning = false, double barSpeed = 1)
    {
        ButtonSlot.Child = button;
        ButtonSlot.Visibility = button is null ? Visibility.Collapsed : Visibility.Visible;
        Headline.Text = headline;
        Detail.Blocks.Clear();
        detail?.Invoke(Detail);
        Detail.Visibility = detail is null ? Visibility.Collapsed : Visibility.Visible;
        WarningIcon.Visibility = warning ? Visibility.Visible : Visibility.Collapsed;
        Bars.Visibility = warning ? Visibility.Collapsed : Visibility.Visible;
        Pill.BorderBrush = (Brush)Application.Current.Resources[warning ? "WarningBrush" : "ArcaneBrush"];

        // Still while nothing's happening, and with Windows' animation effects off; the bars still show it's listening.
        Wave.Stop();
        if (!warning && Settings.AnimationsEnabled)
        {
            Wave.SpeedRatio = barSpeed;
            Wave.Begin();
        }

        Place();
        if (shown) return;
        AppWindow.Show(activateWindow: false);
        shown = true;
    }

    public void Hide()
    {
        if (!shown) return;
        Wave.Stop();
        AppWindow.Hide();
        shown = false;
    }

    /// <summary>Sizes the window to the pill and centres it near the top of the screen the game is on (whatever's in front).</summary>
    void Place()
    {
        var front = Win32Interop.GetWindowIdFromWindow(GetForegroundWindow());
        var screen = DisplayArea.GetFromWindowId(front, DisplayAreaFallback.Primary).OuterBounds;
        int top = screen.Y + (int)(screen.Height * FromTop);
        // Onto that screen first, so the size is worked out at its scaling.
        AppWindow.Move(new PointInt32(screen.X + screen.Width / 2, top));
        double scale = GetDpiForWindow(hwnd) / 96.0;
        Pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        int width = (int)Math.Ceiling(Pill.DesiredSize.Width * scale), height = (int)Math.Ceiling(Pill.DesiredSize.Height * scale);
        AppWindow.MoveAndResize(new RectInt32(screen.X + (screen.Width - width) / 2, top, width, height));
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, int size);
}

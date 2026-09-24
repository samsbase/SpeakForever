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
/// picture on the website, then that the text is ready to paste. It floats over the game without
/// ever taking focus from it, so the game's chat box keeps focus for the paste. Nothing can draw
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
    const uint DWM_BB_ENABLE = 1, DWM_BB_BLURREGION = 2;

    static readonly UISettings Settings = new();

    static readonly double[] BarShape = [0.6, 0.85, 1, 0.85, 0.6]; // tallest in the middle, like the website's

    readonly IntPtr hwnd;
    readonly double[] recent = new double[3]; // the latest loudness first
    bool shown, followVoice;

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
        // Without this Windows fills the window before ClearBackdrop's clear brush, and the pill sits in a dark square.
        // Blur-behind over an empty region blurs nothing but has the window composited with its transparency.
        var blur = new DwmBlurBehind { Flags = DWM_BB_ENABLE | DWM_BB_BLURREGION, Enable = 1, BlurRegion = CreateRectRgn(-2, -2, -1, -1) };
        _ = DwmEnableBlurBehindWindow(hwnd, ref blur);
        DeleteObject(blur.BlurRegion);
    }

    /// <summary>Shows the pill, or changes what it says if it's already up.</summary>
    /// <param name="headline">"Listening", say.</param>
    /// <param name="button">Icons for the button that finishes it, shown first; null for none.</param>
    /// <param name="detail">Fills a smaller line under the headline; null for none.</param>
    /// <param name="glyph">A Segoe Fluent icon shown still, instead of the moving bars; null for the bars.</param>
    /// <param name="warning">In the warning colour.</param>
    /// <param name="barSpeed">How fast the bars move: 1 while listening, faster while transcribing.</param>
    /// <param name="followVoice">The bars rise and fall with the mic (<see cref="ShowLevel"/>) instead of on their own.</param>
    public void Show(string headline, FrameworkElement? button = null, Action<RichTextBlock>? detail = null, string? glyph = null,
        bool warning = false, double barSpeed = 1, bool followVoice = false)
    {
        Fill(headline, button, detail, glyph, warning);

        // Still while nothing's happening, and with Windows' animation effects off; the bars still show it's listening.
        Wave.Stop();
        this.followVoice = followVoice && glyph is null && Settings.AnimationsEnabled;
        if (this.followVoice) ShowLevel(0);
        else if (glyph is null && Settings.AnimationsEnabled)
        {
            Wave.SpeedRatio = barSpeed;
            Wave.Begin();
        }

        // Placed only as it appears: moving or resizing it between states made it flash.
        if (shown) return;
        Place();
        AppWindow.Show(activateWindow: false);
        shown = true;
    }

    /// <summary>
    /// Fixes the pill at the size of the largest of these contents, so changing state only changes
    /// what's inside it. It stays a pill however tall that makes it. Called again when the buttons change.
    /// </summary>
    public void FitTo(params (string Headline, FrameworkElement? Button, Action<RichTextBlock>? Detail, string? Glyph)[] states)
    {
        Pill.Width = Pill.Height = double.NaN; // measured at their own size, not the last fit's
        double width = 0, height = 0;
        foreach (var (headline, button, detail, glyph) in states)
        {
            Fill(headline, button, detail, glyph, warning: false);
            Pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = Math.Max(width, Pill.DesiredSize.Width - Pill.Margin.Left - Pill.Margin.Right);
            height = Math.Max(height, Pill.DesiredSize.Height - Pill.Margin.Top - Pill.Margin.Bottom);
        }
        Pill.Width = width;
        Pill.Height = height;
        Pill.CornerRadius = new CornerRadius(height / 2);
        if (shown) Place(); // refitted while up: the window follows the pill
    }

    void Fill(string headline, FrameworkElement? button, Action<RichTextBlock>? detail, string? glyph, bool warning)
    {
        ButtonSlot.Child = button;
        ButtonSlot.Visibility = button is null ? Visibility.Collapsed : Visibility.Visible;
        Headline.Text = headline;
        Detail.Blocks.Clear();
        detail?.Invoke(Detail);
        Detail.Visibility = detail is null ? Visibility.Collapsed : Visibility.Visible;
        Icon.Glyph = glyph ?? "";
        Icon.Visibility = glyph is null ? Visibility.Collapsed : Visibility.Visible;
        Bars.Visibility = glyph is null ? Visibility.Visible : Visibility.Collapsed;
        var colour = (Brush)Application.Current.Resources[warning ? "WarningBrush" : "ArcaneBrush"];
        Pill.BorderBrush = Icon.Foreground = colour;
    }

    /// <summary>
    /// The mic's loudness, 0 to 1, every 30 ms while listening. The middle bar takes it now and each
    /// pair further out a frame later, so the voice ripples outwards.
    /// </summary>
    public void ShowLevel(double level)
    {
        if (!followVoice) return;
        Array.Copy(recent, 0, recent, 1, recent.Length - 1);
        recent[0] = level;
        ScaleTransform[] bars = [Bar1, Bar2, Bar3, Bar4, Bar5];
        for (int i = 0; i < bars.Length; i++)
            bars[i].ScaleY = 0.15 + 0.85 * BarShape[i] * recent[Math.Abs(i - 2)];
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
        // The client area, not the outer size: a borderless WinUI window still keeps a few pixels of frame, which cropped the pill's bottom.
        AppWindow.Move(new PointInt32(screen.X + (screen.Width - width) / 2, top));
        AppWindow.ResizeClient(new SizeInt32(width, height));
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    struct DwmBlurBehind
    {
        public uint Flags;
        public int Enable;
        public IntPtr BlurRegion;
        public int TransitionOnMaximized;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blurBehind);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, int size);
}

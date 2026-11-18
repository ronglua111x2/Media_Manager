namespace media_management_app.Common;

/// <summary>A rectangle in device pixels (same meaning as the Win32 <c>RECT</c>: Right and Bottom are exclusive).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool Intersects(PixelRect other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
}

/// <summary>Which screen edge the taskbar is docked to. Values match <c>ABE_LEFT..ABE_BOTTOM</c>.</summary>
public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3
}

/// <summary>
/// What a maximized window answers in <c>WM_GETMINMAXINFO</c>: position relative to the monitor's top-left
/// corner, and size, both in device pixels.
/// </summary>
public readonly record struct MaximizeBounds(int X, int Y, int Width, int Height);

/// <summary>Size limits for a window's first open, in DIPs.</summary>
public readonly record struct InitialWindowSize(double Width, double Height, double MinWidth, double MinHeight);

/// <summary>Where to put a window (in DIPs) after it is dragged out of the maximized state.</summary>
public readonly record struct DragRestorePosition(double Left, double Top);

/// <summary>
/// Pure arithmetic behind <see cref="WindowFit"/>. A borderless window (<c>WindowStyle=None</c> + <c>WindowChrome</c>)
/// loses Windows' own maximize handling, so the window has to answer <c>WM_GETMINMAXINFO</c> itself. Nothing in this
/// class touches Win32 or WPF, so every case (taskbar side, auto-hide, DPI) is unit-tested.
/// </summary>
public static class WindowFitMath
{
    /// <summary>Pixels left free on the taskbar edge so an auto-hide taskbar can still pop up.</summary>
    public const int AutoHideGapPx = 2;

    /// <summary>First-open windows keep this many DIPs free around them on small screens.</summary>
    public const double InitialSizeMarginDip = 32;

    public const double MinWidthFloorDip = 480;

    public const double MinHeightFloorDip = 400;

    /// <summary>The caption-ish strip (DIPs) the cursor grabs when a maximized window is dragged back to normal.</summary>
    public const double DragGrabOffsetDip = 20;

    /// <summary>
    /// An auto-hide taskbar makes the work area equal to the whole monitor. Keep <paramref name="gapPx"/> free on the
    /// taskbar's edge, otherwise the maximized window covers the sliver the mouse needs to reveal the taskbar.
    /// Returns <paramref name="work"/> unchanged when there is no auto-hide taskbar on this monitor.
    /// </summary>
    /// <param name="autoHideEdge">The auto-hide taskbar's edge, or null when the taskbar is not auto-hide.</param>
    /// <param name="taskbar">The taskbar rectangle. Used to skip a taskbar that sits on another monitor.</param>
    public static PixelRect ApplyAutoHideGap(
        PixelRect monitor,
        PixelRect work,
        TaskbarEdge? autoHideEdge,
        PixelRect taskbar,
        int gapPx = AutoHideGapPx)
    {
        if (autoHideEdge is null || !taskbar.Intersects(monitor))
        {
            return work;
        }

        return autoHideEdge.Value switch
        {
            TaskbarEdge.Left => work with { Left = Math.Max(work.Left, monitor.Left + gapPx) },
            TaskbarEdge.Top => work with { Top = Math.Max(work.Top, monitor.Top + gapPx) },
            TaskbarEdge.Right => work with { Right = Math.Min(work.Right, monitor.Right - gapPx) },
            _ => work with { Bottom = Math.Min(work.Bottom, monitor.Bottom - gapPx) }
        };
    }

    /// <summary>Maximized position (relative to the monitor) and size: exactly the work area.</summary>
    public static MaximizeBounds ComputeMaximizeBounds(PixelRect monitor, PixelRect work) =>
        new(work.Left - monitor.Left, work.Top - monitor.Top, work.Width, work.Height);

    /// <summary>
    /// DIP minimum to device pixels for <c>ptMinTrackSize</c>. Rounds up, with a tiny tolerance so that
    /// 600 × 1.25 = 750.0000000000001 does not become 751.
    /// </summary>
    public static int ToPixelsCeiling(double dip, double scale) =>
        (int)Math.Ceiling((dip * scale) - 1e-6);

    /// <summary>
    /// First open on a small screen (a 14" laptop at 150% has about 1280×670 DIPs free): never larger than the work
    /// area minus <see cref="InitialSizeMarginDip"/>, so the title bar and the bottom row are never off-screen.
    /// The minimum size is lowered too, otherwise WPF would force the window back above the limit.
    /// </summary>
    public static InitialWindowSize FitInitialSize(
        double width,
        double height,
        double minWidth,
        double minHeight,
        double workWidth,
        double workHeight)
    {
        var maxWidth = Math.Floor(workWidth - InitialSizeMarginDip);
        var maxHeight = Math.Floor(workHeight - InitialSizeMarginDip);

        if (minWidth > maxWidth)
        {
            minWidth = Math.Max(MinWidthFloorDip, maxWidth);
        }

        if (minHeight > maxHeight)
        {
            minHeight = Math.Max(MinHeightFloorDip, maxHeight);
        }

        if (width > maxWidth)
        {
            width = Math.Max(minWidth, maxWidth);
        }

        if (height > maxHeight)
        {
            height = Math.Max(minHeight, maxHeight);
        }

        return new InitialWindowSize(width, height, minWidth, minHeight);
    }

    /// <summary>
    /// Window position (DIPs) when the user drags the title bar of a maximized window: the restored window keeps the
    /// cursor at the same horizontal fraction of its title bar and grabs it <see cref="DragGrabOffsetDip"/> from the top.
    /// Inputs are device pixels (cursor, maximized window, work area of the monitor under the cursor) and DIPs
    /// (restored size); the result is converted with <paramref name="scale"/> so pixels are never mixed with DIPs.
    /// The position is clamped to the work area of the cursor's monitor, so it works with a taskbar on any edge and
    /// with monitors at negative coordinates.
    /// </summary>
    public static DragRestorePosition ComputeDragRestorePosition(
        int cursorX,
        int cursorY,
        PixelRect maximizedWindow,
        double restoredWidthDip,
        double restoredHeightDip,
        double scale,
        PixelRect work)
    {
        if (scale <= 0)
        {
            scale = 1;
        }

        var ratio = maximizedWindow.Width > 0
            ? Math.Clamp((cursorX - maximizedWindow.Left) / (double)maximizedWindow.Width, 0, 1)
            : 0.5;

        var restoredWidthPx = restoredWidthDip * scale;
        var restoredHeightPx = restoredHeightDip * scale;
        var grabOffsetPx = DragGrabOffsetDip * scale;

        var left = cursorX - (restoredWidthPx * ratio);
        left = Math.Min(left, work.Right - restoredWidthPx);
        left = Math.Max(left, work.Left);

        var top = cursorY - grabOffsetPx;
        // Keep at least the grab strip (twice the offset) above the bottom edge so the title bar stays reachable.
        top = Math.Min(top, work.Bottom - Math.Min(restoredHeightPx, grabOffsetPx * 2));
        top = Math.Max(top, work.Top);

        return new DragRestorePosition(left / scale, top / scale);
    }
}

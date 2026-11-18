using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace media_management_app.Common;

/// <summary>
/// Fixes for windows that draw their own title bar (<c>WindowStyle=None</c> + <c>WindowChrome</c>). Windows no longer
/// does two things for them:
/// <list type="bullet">
/// <item>Keep a maximized window inside the work area. Without this it covers the taskbar and cuts off the bottom row.
/// With an auto-hide taskbar it must leave a sliver free, or the taskbar can never pop up.</item>
/// <item>Fit the first size to a small screen.</item>
/// </list>
/// Call <see cref="Attach"/> once, right after <c>InitializeComponent()</c>. The arithmetic lives in
/// <see cref="WindowFitMath"/>; this class is the thin Win32 layer around it.
/// </summary>
public static class WindowFit
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 2;
    private const uint AbmGetState = 4;
    private const uint AbmGetTaskbarPos = 5;
    private const uint AbsAutoHide = 1;

    /// <summary>Limits the first size to the primary work area and hooks <c>WM_GETMINMAXINFO</c> once the window has a handle.</summary>
    public static void Attach(Window window)
    {
        FitInitialSize(window);
        window.SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)
                ?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                    OnWindowMessage(window, hwnd, msg, lParam, ref handled));
    }

    /// <summary>
    /// Drag start on the title bar of a maximized window: restore it and put it under the cursor. Everything is computed
    /// in device pixels and converted once with the window's DPI scale (UI-10: the old code mixed pixels and DIPs and forced
    /// <c>Top &gt;= 0</c>). Does nothing unless the window is maximized. The caller still calls <c>DragMove()</c> afterwards.
    /// </summary>
    public static void RestoreFromMaximizedForDrag(Window window)
    {
        if (window.WindowState != WindowState.Maximized)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetCursorPos(out var cursor) || !GetWindowRect(hwnd, out var windowRect))
        {
            return;
        }

        var scale = DpiScale(window);
        var restored = window.RestoreBounds;
        var workPx = GetWorkAreaAt(cursor) ?? ToPixelRect(windowRect);

        var position = WindowFitMath.ComputeDragRestorePosition(
            cursor.X,
            cursor.Y,
            ToPixelRect(windowRect),
            restored.Width,
            restored.Height,
            scale,
            workPx);

        window.WindowState = WindowState.Normal;
        window.Width = restored.Width;
        window.Height = restored.Height;
        window.Left = position.Left;
        window.Top = position.Top;
    }

    private static void FitInitialSize(Window window)
    {
        var work = SystemParameters.WorkArea; // primary screen, DIPs: the window opens centered there
        var fitted = WindowFitMath.FitInitialSize(
            window.Width,
            window.Height,
            window.MinWidth,
            window.MinHeight,
            work.Width,
            work.Height);

        window.MinWidth = fitted.MinWidth;
        window.MinHeight = fitted.MinHeight;
        window.Width = fitted.Width;
        window.Height = fitted.Height;
    }

    private static IntPtr OnWindowMessage(Window window, IntPtr hwnd, int msg, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        try
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            {
                return IntPtr.Zero;
            }

            var monitorRect = ToPixelRect(info.Monitor);
            var work = ApplyAutoHide(monitorRect, ToPixelRect(info.Work));
            var bounds = WindowFitMath.ComputeMaximizeBounds(monitorRect, work);

            var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            minMax.MaxPosition = new NativePoint { X = bounds.X, Y = bounds.Y };
            minMax.MaxSize = new NativePoint { X = bounds.Width, Y = bounds.Height };
            // We answer the message ourselves, so the minimum size goes in here too.
            var scale = DpiScale(window);
            minMax.MinTrackSize = new NativePoint
            {
                X = WindowFitMath.ToPixelsCeiling(window.MinWidth, scale),
                Y = WindowFitMath.ToPixelsCeiling(window.MinHeight, scale)
            };
            Marshal.StructureToPtr(minMax, lParam, true);
            handled = true;
        }
        catch (Exception ex)
        {
            // Falling back to the default maximize behaviour is better than crashing a window message.
            System.Diagnostics.Debug.WriteLine($"WindowFit: WM_GETMINMAXINFO failed: {ex.Message}");
        }

        return IntPtr.Zero;
    }

    private static PixelRect ApplyAutoHide(PixelRect monitor, PixelRect work)
    {
        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
        if (((uint)SHAppBarMessage(AbmGetState, ref data) & AbsAutoHide) == 0)
        {
            return work;
        }

        if (SHAppBarMessage(AbmGetTaskbarPos, ref data) == UIntPtr.Zero)
        {
            return work;
        }

        return WindowFitMath.ApplyAutoHideGap(monitor, work, (TaskbarEdge)data.Edge, ToPixelRect(data.Rect));
    }

    private static PixelRect? GetWorkAreaAt(NativePoint point)
    {
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info)
            ? ToPixelRect(info.Work)
            : null;
    }

    private static double DpiScale(Window window)
    {
        try
        {
            return VisualTreeHelper.GetDpi(window).DpiScaleX;
        }
        catch (InvalidOperationException)
        {
            return 1.0;
        }
    }

    private static PixelRect ToPixelRect(NativeRect r) => new(r.Left, r.Top, r.Right, r.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Hwnd;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public IntPtr LParam;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
}

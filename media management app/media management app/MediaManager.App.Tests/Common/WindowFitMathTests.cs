using media_management_app.Common;

namespace MediaManager.App.Tests.Common;

public class WindowFitMathTests
{
    private static readonly PixelRect Monitor1080 = new(0, 0, 1920, 1080);

    private static readonly PixelRect NoTaskbar = new(-1, -1, -1, -1); // never intersects a monitor

    // ---- maximize bounds: the taskbar on each edge ----

    [Fact]
    public void Maximize_TaskbarBottom_FillsWorkArea()
    {
        var bounds = WindowFitMath.ComputeMaximizeBounds(Monitor1080, new PixelRect(0, 0, 1920, 1032));

        Assert.Equal(new MaximizeBounds(0, 0, 1920, 1032), bounds);
    }

    [Fact]
    public void Maximize_TaskbarLeft_StartsAfterTaskbar()
    {
        var bounds = WindowFitMath.ComputeMaximizeBounds(Monitor1080, new PixelRect(60, 0, 1920, 1080));

        Assert.Equal(new MaximizeBounds(60, 0, 1860, 1080), bounds);
    }

    [Fact]
    public void Maximize_TaskbarTop_StartsBelowTaskbar()
    {
        var bounds = WindowFitMath.ComputeMaximizeBounds(Monitor1080, new PixelRect(0, 48, 1920, 1080));

        Assert.Equal(new MaximizeBounds(0, 48, 1920, 1032), bounds);
    }

    [Fact]
    public void Maximize_TaskbarRight_EndsBeforeTaskbar()
    {
        var bounds = WindowFitMath.ComputeMaximizeBounds(Monitor1080, new PixelRect(0, 0, 1860, 1080));

        Assert.Equal(new MaximizeBounds(0, 0, 1860, 1080), bounds);
    }

    [Fact]
    public void Maximize_PositionIsRelativeToTheMonitor_OnASecondaryMonitorAbovePrimary()
    {
        // Monitor placed above the primary one (negative Y), taskbar on its bottom edge.
        var monitor = new PixelRect(1920, -1440, 4480, 0);
        var work = new PixelRect(1920, -1440, 4480, -48);

        var bounds = WindowFitMath.ComputeMaximizeBounds(monitor, work);

        Assert.Equal(new MaximizeBounds(0, 0, 2560, 1392), bounds);
    }

    [Fact]
    public void Maximize_PositionIsRelativeToTheMonitor_OnAMonitorLeftOfPrimaryWithTaskbarOnTheLeft()
    {
        var monitor = new PixelRect(-1920, 0, 0, 1080);
        var work = new PixelRect(-1860, 0, 0, 1080);

        var bounds = WindowFitMath.ComputeMaximizeBounds(monitor, work);

        Assert.Equal(new MaximizeBounds(60, 0, 1860, 1080), bounds);
    }

    // ---- auto-hide taskbar ----

    [Theory]
    [InlineData(TaskbarEdge.Left, 2, 0, 1920, 1080)]
    [InlineData(TaskbarEdge.Top, 0, 2, 1920, 1080)]
    [InlineData(TaskbarEdge.Right, 0, 0, 1918, 1080)]
    [InlineData(TaskbarEdge.Bottom, 0, 0, 1920, 1078)]
    public void AutoHide_LeavesTwoPixelsFreeOnTheTaskbarEdge(
        TaskbarEdge edge, int left, int top, int right, int bottom)
    {
        // An auto-hide taskbar leaves work area == monitor.
        var taskbar = edge switch
        {
            TaskbarEdge.Left => new PixelRect(0, 0, 2, 1080),
            TaskbarEdge.Top => new PixelRect(0, 0, 1920, 2),
            TaskbarEdge.Right => new PixelRect(1918, 0, 1920, 1080),
            _ => new PixelRect(0, 1078, 1920, 1080)
        };

        var work = WindowFitMath.ApplyAutoHideGap(Monitor1080, Monitor1080, edge, taskbar);

        Assert.Equal(new PixelRect(left, top, right, bottom), work);
    }

    [Fact]
    public void AutoHide_BottomTaskbar_MaximizeBoundsStopTwoPixelsShort()
    {
        var taskbar = new PixelRect(0, 1078, 1920, 1080);

        var work = WindowFitMath.ApplyAutoHideGap(Monitor1080, Monitor1080, TaskbarEdge.Bottom, taskbar);
        var bounds = WindowFitMath.ComputeMaximizeBounds(Monitor1080, work);

        Assert.Equal(new MaximizeBounds(0, 0, 1920, 1078), bounds);
    }

    [Fact]
    public void AutoHide_NotAutoHide_ReturnsWorkAreaUntouched()
    {
        var work = new PixelRect(0, 0, 1920, 1032);

        var result = WindowFitMath.ApplyAutoHideGap(Monitor1080, work, null, new PixelRect(0, 1032, 1920, 1080));

        Assert.Equal(work, result);
    }

    [Fact]
    public void AutoHide_TaskbarOnAnotherMonitor_ReturnsWorkAreaUntouched()
    {
        var secondary = new PixelRect(1920, 0, 3840, 1080);
        var taskbarOnPrimary = new PixelRect(0, 1078, 1920, 1080);

        var result = WindowFitMath.ApplyAutoHideGap(secondary, secondary, TaskbarEdge.Bottom, taskbarOnPrimary);

        Assert.Equal(secondary, result);
    }

    [Fact]
    public void AutoHide_NeverGivesBackSpaceAnAppbarAlreadyTook()
    {
        // A non-auto-hide appbar already took 40 px at the bottom: the 2 px gap must not give space back.
        var work = new PixelRect(0, 0, 1920, 1040);

        var result = WindowFitMath.ApplyAutoHideGap(
            Monitor1080, work, TaskbarEdge.Bottom, new PixelRect(0, 1078, 1920, 1080));

        Assert.Equal(work, result);
    }

    [Fact]
    public void AutoHide_UnusableTaskbarRect_IsIgnored()
    {
        var result = WindowFitMath.ApplyAutoHideGap(Monitor1080, Monitor1080, TaskbarEdge.Bottom, NoTaskbar);

        Assert.Equal(Monitor1080, result);
    }

    // ---- DIP -> pixel for ptMinTrackSize ----

    [Theory]
    [InlineData(960, 1.0, 960)]
    [InlineData(960, 1.5, 1440)]
    [InlineData(600, 1.25, 750)]   // 600 * 1.25 must not round up to 751
    [InlineData(601, 1.25, 752)]   // 751.25 rounds up
    [InlineData(800, 1.75, 1400)]
    [InlineData(480, 2.0, 960)]
    public void ToPixelsCeiling_ScalesAndRoundsUp(double dip, double scale, int expected)
    {
        Assert.Equal(expected, WindowFitMath.ToPixelsCeiling(dip, scale));
    }

    // ---- first-open size on small screens ----

    [Fact]
    public void FitInitialSize_BigScreen_ChangesNothing()
    {
        var fitted = WindowFitMath.FitInitialSize(1280, 720, 960, 600, 1920, 1032);

        Assert.Equal(new InitialWindowSize(1280, 720, 960, 600), fitted);
    }

    [Fact]
    public void FitInitialSize_14InchLaptopAt150Percent_ShrinksTheWindow()
    {
        // 1920x1080 at 150% -> 1280x720 DIPs, taskbar takes ~32: about 1280x688 free.
        var fitted = WindowFitMath.FitInitialSize(1280, 720, 960, 600, 1280, 688);

        Assert.Equal(1248, fitted.Width);   // 1280 - 32
        Assert.Equal(656, fitted.Height);   // 688 - 32
        Assert.Equal(960, fitted.MinWidth);
        Assert.Equal(600, fitted.MinHeight);
    }

    [Fact]
    public void FitInitialSize_TinyScreen_LowersMinimumSizeToo()
    {
        var fitted = WindowFitMath.FitInitialSize(1280, 720, 960, 600, 800, 500);

        Assert.Equal(new InitialWindowSize(768, 468, 768, 468), fitted);
    }

    [Fact]
    public void FitInitialSize_NeverGoesBelowTheMinimumFloors()
    {
        var fitted = WindowFitMath.FitInitialSize(1280, 720, 960, 600, 400, 300);

        Assert.Equal(WindowFitMath.MinWidthFloorDip, fitted.MinWidth);
        Assert.Equal(WindowFitMath.MinHeightFloorDip, fitted.MinHeight);
        Assert.True(fitted.Width >= fitted.MinWidth);
        Assert.True(fitted.Height >= fitted.MinHeight);
    }

    // ---- dragging a maximized window (UI-10) ----

    [Fact]
    public void DragRestore_100Percent_KeepsCursorFractionAndGrabsTitleBar()
    {
        var work = new PixelRect(0, 0, 1920, 1032);

        var position = WindowFitMath.ComputeDragRestorePosition(
            cursorX: 960, cursorY: 10, maximizedWindow: work, 1280, 720, scale: 1.0, work);

        Assert.Equal(320, position.Left);   // 960 - 1280 * 0.5
        Assert.Equal(0, position.Top);      // 10 - 20 clamped to the work area top
    }

    [Fact]
    public void DragRestore_150Percent_ReturnsDipsNotPixels()
    {
        // 2880x1620 screen at 150%, taskbar at the bottom: work 2880x1572. Restored size is 1280x720 DIPs.
        var work = new PixelRect(0, 0, 2880, 1572);

        var position = WindowFitMath.ComputeDragRestorePosition(
            cursorX: 1440, cursorY: 45, maximizedWindow: work, 1280, 720, scale: 1.5, work);

        // Cursor is at the middle: left = 1440 - 1920 * 0.5 = 480 px = 320 DIPs. The old code returned 480.
        Assert.Equal(320, position.Left);
        Assert.Equal(10, position.Top);     // (45 - 30) px = 15 px = 10 DIPs
    }

    [Fact]
    public void DragRestore_CursorNearLeftEdge_KeepsTheSameFraction()
    {
        var work = new PixelRect(0, 0, 1920, 1032);

        var position = WindowFitMath.ComputeDragRestorePosition(
            cursorX: 96, cursorY: 15, maximizedWindow: work, 1280, 720, scale: 1.0, work);

        // ratio 0.05 -> left = 96 - 64 = 32
        Assert.Equal(32, position.Left);
    }

    [Fact]
    public void DragRestore_TaskbarOnTop_DoesNotPlaceTheWindowUnderTheTaskbar()
    {
        var work = new PixelRect(0, 48, 1920, 1080);

        var position = WindowFitMath.ComputeDragRestorePosition(
            cursorX: 960, cursorY: 60, maximizedWindow: work, 1280, 720, scale: 1.0, work);

        Assert.Equal(48, position.Top);     // 60 - 20 = 40 would sit under the taskbar
    }

    [Fact]
    public void DragRestore_MonitorAbovePrimary_UsesNegativeCoordinates()
    {
        // Secondary monitor above the primary one: Y runs from -1440 to 0. The old Math.Max(0, ...) pushed the
        // window down to the primary monitor.
        var work = new PixelRect(1920, -1440, 4480, -48);

        var position = WindowFitMath.ComputeDragRestorePosition(
            cursorX: 3200, cursorY: -1400, maximizedWindow: work, 1280, 720, scale: 1.0, work);

        Assert.Equal(2560, position.Left);  // 3200 - 1280 * 0.5
        Assert.Equal(-1420, position.Top);  // -1400 - 20, inside the work area
    }

    [Fact]
    public void DragRestore_MonitorAbovePrimary_ClampsToItsOwnTopEdge()
    {
        var work = new PixelRect(1920, -1440, 4480, -48);

        var position = WindowFitMath.ComputeDragRestorePosition(
            cursorX: 3200, cursorY: -1435, maximizedWindow: work, 1280, 720, scale: 1.0, work);

        Assert.Equal(-1440, position.Top);
    }

    [Fact]
    public void DragRestore_RestoredWindowWiderThanWorkArea_AnchorsToLeftEdge()
    {
        var work = new PixelRect(0, 0, 1920, 1032);

        var position = WindowFitMath.ComputeDragRestorePosition(
            cursorX: 1900, cursorY: 10, maximizedWindow: work, 3000, 720, scale: 1.0, work);

        Assert.Equal(0, position.Left);
    }

    [Fact]
    public void DragRestore_CursorOnFarRight_KeepsTheWindowInsideTheWorkArea()
    {
        var work = new PixelRect(0, 0, 1920, 1032);

        var position = WindowFitMath.ComputeDragRestorePosition(
            cursorX: 1919, cursorY: 10, maximizedWindow: work, 1280, 720, scale: 1.0, work);

        Assert.Equal(1919 - (1280 * (1919 / 1920.0)), position.Left, precision: 6);
        Assert.True(position.Left + 1280 <= work.Right);
    }

    [Fact]
    public void DragRestore_DegenerateInputs_DoNotThrow()
    {
        var work = new PixelRect(0, 0, 1920, 1032);

        var position = WindowFitMath.ComputeDragRestorePosition(
            cursorX: 100, cursorY: 100, maximizedWindow: new PixelRect(0, 0, 0, 0), 800, 600, scale: 0, work);

        Assert.Equal(0, position.Left);     // ratio 0.5 -> 100 - 400, clamped to the left edge
        Assert.Equal(80, position.Top);
    }
}

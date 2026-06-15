using System.Diagnostics;
using System.Runtime.InteropServices;

namespace media_management_app.Services;

public sealed class AppLifecycleService : IAppLifecycleService
{
    private static readonly TimeSpan BackgroundDebounce = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TrimDelay = TimeSpan.FromSeconds(2);

    private CancellationTokenSource? _backgroundCts;
    private CancellationTokenSource? _trimCts;

    public AppMode CurrentMode { get; private set; } = AppMode.Foreground;

    public event EventHandler<AppMode>? AppModeChanged;

    public void EnterBackgroundMode()
    {
        if (CurrentMode == AppMode.Background)
        {
            return;
        }

        CancelPendingBackground();
        var cts = new CancellationTokenSource();
        _backgroundCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(BackgroundDebounce, cts.Token);
                Interlocked.CompareExchange(ref _backgroundCts, null, cts);

                CurrentMode = AppMode.Background;
                AppModeChanged?.Invoke(this, AppMode.Background);
                ScheduleTrimWorkingSet();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.CompareExchange(ref _backgroundCts, null, cts);
                cts.Dispose();
            }
        }, cts.Token);
    }

    public void EnterForegroundMode()
    {
        CancelPendingBackground();
        CancelPendingTrim();

        if (CurrentMode == AppMode.Foreground)
        {
            return;
        }

        CurrentMode = AppMode.Foreground;
        AppModeChanged?.Invoke(this, AppMode.Foreground);
    }

    private void CancelPendingBackground()
    {
        var cts = Interlocked.Exchange(ref _backgroundCts, null);
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void CancelPendingTrim()
    {
        var cts = Interlocked.Exchange(ref _trimCts, null);
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ScheduleTrimWorkingSet()
    {
        CancelPendingTrim();
        var cts = new CancellationTokenSource();
        _trimCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TrimDelay, cts.Token);
                TrimWorkingSet();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.CompareExchange(ref _trimCts, null, cts);
                cts.Dispose();
            }
        }, cts.Token);
    }

    private void TrimWorkingSet()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);

        try
        {
            SetProcessWorkingSetSize(
                Process.GetCurrentProcess().Handle,
                (IntPtr)(-1),
                (IntPtr)(-1));
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(
        IntPtr process,
        IntPtr minimumWorkingSetSize,
        IntPtr maximumWorkingSetSize);
}

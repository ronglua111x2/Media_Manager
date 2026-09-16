using System.Windows.Threading;

namespace media_management_app.Common;

/// <summary>
/// Shows a busy overlay, yields one dispatcher frame so it can paint, then runs UI-thread work.
/// Each workspace VM owns an instance so a newer request or <see cref="Cancel"/> drops an older callback.
/// </summary>
public sealed class UiBusyWork
{
    private int _generation;
    private CancellationTokenSource? _cts;

    public void Cancel()
    {
        _generation++;
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Run(Action<bool> setBusy, bool showOverlay, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Run(setBusy, showOverlay, _ =>
        {
            work();
            return Task.CompletedTask;
        });
    }

    public void Run(Action<bool> setBusy, bool showOverlay, Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(setBusy);
        ArgumentNullException.ThrowIfNull(work);

        var token = ++_generation;
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        var cts = new CancellationTokenSource();
        _cts = cts;

        if (showOverlay)
        {
            setBusy(true);
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            async () =>
            {
                if (token != _generation)
                {
                    return;
                }

                try
                {
                    await work(cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    if (token == _generation)
                    {
                        setBusy(false);
                    }
                }
            });
    }
}

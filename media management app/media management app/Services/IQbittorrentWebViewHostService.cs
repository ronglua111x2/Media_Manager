using WpfPanel = System.Windows.Controls.Panel;

namespace media_management_app.Services;

public interface IQbittorrentWebViewHostService
{
    event EventHandler<QbittorrentWebViewStatusChangedEventArgs>? StatusChanged;

    string? CurrentUrl { get; }

    Task InitializeAsync(WpfPanel host, CancellationToken cancellationToken = default);

    Task NavigateToConfiguredUrlAsync(CancellationToken cancellationToken = default);

    void Reload();
}

public sealed class QbittorrentWebViewStatusChangedEventArgs : EventArgs
{
    public required string Message { get; init; }

    public bool IsLoading { get; init; }

    public bool IsReady { get; init; }

    public bool HasError { get; init; }

    public string? CurrentUrl { get; init; }
}

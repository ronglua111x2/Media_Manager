namespace media_management_app.Services;

public interface IWarpCliService
{
    bool IsAvailable { get; }

    string ResolvedExecutablePath { get; }

    Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    Task<bool> IsConnectedAsync(CancellationToken ct = default);
}

namespace media_management_app.Services;

public interface IWarpCliService
{
    bool IsAvailable { get; }

    string ResolvedExecutablePath { get; }

    Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// Ensures WARP is connected. Owned is true only when this call initiated the connect
    /// (not when WARP was already connected).
    /// </summary>
    Task<WarpConnectAttempt> ConnectOwnedAsync(TimeSpan timeout, CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    Task<bool> IsConnectedAsync(CancellationToken ct = default);
}

public readonly record struct WarpConnectAttempt(bool Connected, bool Owned);

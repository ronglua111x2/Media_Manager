namespace media_management_app.Services;

public enum WarpLeaseReason
{
    User = 0,
    TmdbSslRecover = 1,
    Hunt = 2,
    JellyfinHold = 3,
    SettingsTest = 4
}

public enum WarpConnectionChangeSource
{
    Cli = 0,
    User = 1,
    LogTail = 2,
    Poll = 3
}

public sealed class WarpConnectionChangedEventArgs : EventArgs
{
    public bool IsConnected { get; init; }

    public IReadOnlySet<WarpLeaseReason> Leases { get; init; } = new HashSet<WarpLeaseReason>();

    public bool InFlight { get; init; }

    public WarpConnectionChangeSource Source { get; init; }
}

public readonly record struct WarpLeaseResult(bool Connected, bool StartedByThisAcquire);

public interface IWarpCliService
{
    bool IsAvailable { get; }

    string ResolvedExecutablePath { get; }

    bool InFlight { get; }

    bool LastKnownConnected { get; }

    IReadOnlySet<WarpLeaseReason> ActiveLeases { get; }

    bool HasAutoTrackPipelineLease { get; }

    event EventHandler<WarpConnectionChangedEventArgs>? ConnectionChanged;

    Task<WarpLeaseResult> AcquireAsync(
        WarpLeaseReason reason,
        TimeSpan timeout,
        CancellationToken ct = default);

    Task ReleaseAsync(WarpLeaseReason reason, CancellationToken ct = default);

    Task ForceDisconnectAsync(CancellationToken ct = default);

    Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// Ensures WARP is connected. Owned is true only when this call initiated the connect
    /// (not when WARP was already connected). Prefer <see cref="AcquireAsync"/>.
    /// </summary>
    Task<WarpConnectAttempt> ConnectOwnedAsync(TimeSpan timeout, CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    Task<bool> IsConnectedAsync(CancellationToken ct = default);
}

public readonly record struct WarpConnectAttempt(bool Connected, bool Owned);

public static class WarpLeaseReasonText
{
    public static bool IsAutoTrackPipeline(WarpLeaseReason reason) =>
        reason is WarpLeaseReason.TmdbSslRecover or WarpLeaseReason.Hunt or WarpLeaseReason.JellyfinHold;

    public static string Label(WarpLeaseReason reason) => reason switch
    {
        WarpLeaseReason.User => "you",
        WarpLeaseReason.TmdbSslRecover => "Auto-Track TMDB recover",
        WarpLeaseReason.Hunt => "Auto-Track hunt",
        WarpLeaseReason.JellyfinHold => "Jellyfin hold",
        WarpLeaseReason.SettingsTest => "connection test",
        _ => reason.ToString()
    };

    public static string Describe(IReadOnlyCollection<WarpLeaseReason> leases)
    {
        var pipeline = leases
            .Where(IsAutoTrackPipeline)
            .Select(Label)
            .ToList();
        if (pipeline.Count > 0)
        {
            return string.Join(", ", pipeline);
        }

        if (leases.Contains(WarpLeaseReason.User))
        {
            return "you";
        }

        if (leases.Contains(WarpLeaseReason.SettingsTest))
        {
            return "connection test";
        }

        return string.Empty;
    }
}

using media_management_app.Models;

namespace media_management_app.Services;

public enum QbittorrentRestartOutcomeKind
{
    NotAttempted = 0,
    Succeeded = 1,
    Failed = 2,
    PortConflict = 3,
    SkippedCooldown = 4,
    SkippedDisabled = 5,
    SkippedNoProcess = 6,
    SkippedAuthOrUrl = 7,
    SkippedInFlight = 8
}

public sealed class QbittorrentRestartOutcome
{
    public required QbittorrentRestartOutcomeKind Kind { get; init; }

    public string? Detail { get; init; }

    public int? BeforePid { get; init; }

    public int? AfterPid { get; init; }

    public bool WebUiOk { get; init; }

    public bool ShouldAbortHunt => Kind is not QbittorrentRestartOutcomeKind.Succeeded;

    public static QbittorrentRestartOutcome NotAttempted(string detail) => new()
    {
        Kind = QbittorrentRestartOutcomeKind.NotAttempted,
        Detail = detail
    };
}

public interface IQbittorrentProcessRestartService
{
    /// <summary>
    /// Ensures WebUI is usable for hunt. May attempt opt-in process restart only for
    /// unreachable + qbittorrent process running (bind-fail). Never restarts for auth/URL errors.
    /// </summary>
    Task<(QbittorrentWebUiProbeResult Probe, QbittorrentRestartOutcome? Restart)> EnsureWebUiForHuntAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Probe only — used by reconcile. Never restarts.
    /// </summary>
    Task<QbittorrentWebUiProbeResult> ProbeAsync(CancellationToken cancellationToken = default);
}

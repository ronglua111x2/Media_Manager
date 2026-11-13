namespace media_management_app.Common;

/// <summary>
/// Whether a TMDB failure looks like this network blocking TMDB, so a WARP tunnel may help.
/// Thin wrapper over <see cref="NetworkFailureClassifier"/>.
/// </summary>
public static class TmdbNetworkBlockDetector
{
    public static bool IsLikelyBlocked(Exception exception, CancellationToken cancellationToken = default)
    {
        return Describe(exception, cancellationToken) is not null;
    }

    /// <summary>Short reason label for logs, or null when the error is not a network block.</summary>
    public static string? Describe(Exception exception, CancellationToken cancellationToken = default)
    {
        var failure = NetworkFailureClassifier.Classify(exception, cancellationToken);
        return NetworkFailureClassifier.IsWarpFallbackCandidate(failure) ? failure.ToString() : null;
    }
}

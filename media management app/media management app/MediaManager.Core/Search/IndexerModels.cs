using media_management_app.Common;

namespace media_management_app.Search;

[Flags]
public enum IndexerMediaKinds
{
    None = 0,
    Movie = 1,
    TvEpisode = 2,
    TvSeasonPack = 4,
    Anime = 8,
    All = Movie | TvEpisode | TvSeasonPack | Anime
}

public enum IndexerTrust
{
    Low,
    Normal,
    High
}

/// <summary>What an indexer can do. The orchestrator only sends queries an indexer can serve.</summary>
public sealed record IndexerCapabilities(
    IndexerMediaKinds Kinds,
    bool SupportsFreeText = true,
    bool SupportsImdbLookup = false,
    bool IsMetaIndexer = false,
    bool IsDhtIndex = false,
    IndexerTrust Trust = IndexerTrust.Normal);

/// <summary>One query sent to one indexer.</summary>
public sealed record IndexerQuery(
    string Text,
    MediaKind Kind,
    bool IsAnime = false,
    string? ImdbId = null,
    int? Season = null,
    int? Episode = null);

/// <summary>One raw row parsed from an indexer response.</summary>
public sealed record IndexerResult(
    string Title,
    long SizeBytes,
    int Seeders,
    int Leechers,
    string? InfoHash,
    string? MagnetUri,
    string? TorrentUrl,
    string? DetailsUrl,
    DateTimeOffset? PublishedUtc,
    string IndexerId,
    string? Source = null,
    double? VirusProbability = null);

public enum IndexerFailureKind
{
    None,
    RateLimited,
    Blocked,
    Timeout,
    ServerError,
    BadResponse,
    Network
}

/// <summary>The indexer answered with a bot challenge / 403: stop calling it, never try to get around it.</summary>
public sealed class IndexerBlockedException(string indexerId, string detail)
    : Exception($"{indexerId} blocked the request: {detail}")
{
    public string IndexerId { get; } = indexerId;
}

/// <summary>The governor refused to send a request (breaker open, budget used, kill switch...).</summary>
public sealed class IndexerUnavailableException(string indexerId, string reason)
    : Exception($"{indexerId} unavailable: {reason}")
{
    public string IndexerId { get; } = indexerId;

    public string Reason { get; } = reason;
}

public sealed class IndexerHttpException(string indexerId, IndexerFailureKind kind, string detail, TimeSpan? retryAfter = null)
    : Exception($"{indexerId}: {kind} {detail}")
{
    public string IndexerId { get; } = indexerId;

    public IndexerFailureKind Kind { get; } = kind;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}

namespace media_management_app.Search;

/// <summary>
/// A native search source (native-search P2, docs/planning/native-search/01-architecture.md).
/// Implementations only build requests and parse responses: all network access goes through
/// <see cref="IIndexerHttp"/>, which applies the per-site request governor, cache and breaker.
/// </summary>
public interface ITorrentIndexer
{
    /// <summary>Stable id used in settings, governor state and logs ("knaben", "apibay", "nyaa"...).</summary>
    string Id { get; }

    /// <summary>
    /// Name shown as the candidate's engine. Kept equal to the old qBittorrent plugin name where one
    /// existed ("thepiratebay", "nyaasi", "limetorrents") so recipe engine priorities keep working.
    /// </summary>
    string EngineName { get; }

    IndexerCapabilities Capabilities { get; }

    Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken);
}

/// <summary>The only way an indexer talks to the network.</summary>
public interface IIndexerHttp
{
    Task<IndexerHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken);

    Task<IndexerHttpResponse> PostJsonAsync(Uri uri, string json, CancellationToken cancellationToken);

    /// <summary>
    /// Called by the orchestrator after the indexer parsed (or failed to parse) the responses of one query.
    /// A site counts as healthy only once its answer was understood: a 200 "soft block" page that no parser
    /// recognizes must count towards the breaker (audit 07c NS-04).
    /// </summary>
    void ReportParsed(bool parsed)
    {
    }
}

public sealed record IndexerHttpResponse(int StatusCode, string Body, string? ContentType, Uri FinalUri);

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace media_management_app.Search;

/// <summary>Recognizes bot-challenge / block pages. A match means "stop calling this site", never "get around it".</summary>
public static class ChallengeDetector
{
    private static readonly Regex Markers = new(
        @"Just a moment\.\.\.|cf-chl|<title>One moment|Making sure you&#39;re not a bot|Making sure you're not a bot|DDoS-Guard|Checking your browser|cf-turnstile|g-recaptcha|hcaptcha",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool LooksLikeChallenge(string? body, string? contentType)
    {
        if (string.IsNullOrEmpty(body))
        {
            return false;
        }

        // RSS/XML documents are never challenge pages, even when served as text/html with Cloudflare's
        // passive "challenge-platform/scripts/jsd" beacon appended (torrentdownloads does this).
        var start = body.TrimStart();
        if (start.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || start.StartsWith("<rss", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // JSON/RSS APIs are never challenge pages; only inspect HTML-ish bodies.
        if (contentType is not null &&
            (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return Markers.IsMatch(body.Length > 20_000 ? body[..20_000] : body);
    }
}

/// <summary>
/// Short-lived response cache with single-flight: concurrent identical requests share one network call,
/// and a repeated query (Auto-Track runs the same searches often) is answered from memory.
/// </summary>
public sealed class IndexerResponseCache(TimeProvider? time = null)
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private DateTimeOffset _nextSweep = DateTimeOffset.MinValue;

    public int Count => _entries.Count;

    /// <summary>
    /// The cached response for <paramref name="key"/>, or the result of <paramref name="factory"/>. Callers that join
    /// a request another caller started wait with their own <paramref name="cancellationToken"/>; if that request is
    /// cancelled by its starter (its budget or Cancel button), they start their own (audit 07c NS-06).
    /// <paramref name="fromNetwork"/> runs when this call's own factory produced the response.
    /// </summary>
    public async Task<IndexerHttpResponse> GetOrAddAsync(
        string key,
        TimeSpan ttl,
        Func<Task<IndexerHttpResponse>> factory,
        CancellationToken cancellationToken = default,
        Action? fromNetwork = null)
    {
        while (true)
        {
            var now = _time.GetUtcNow();
            SweepExpired(now);
            var mine = new Entry(new Lazy<Task<IndexerHttpResponse>>(factory), now + ttl);
            var entry = _entries.GetOrAdd(key, mine);
            var owned = ReferenceEquals(entry, mine);
            if (entry.ExpiresUtc <= now && entry.Value.IsValueCreated && entry.Value.Value.IsCompleted)
            {
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                continue;
            }

            try
            {
                var response = await entry.Value.Value.WaitAsync(cancellationToken);
                if (owned)
                {
                    fromNetwork?.Invoke();
                }

                return response;
            }
            catch (OperationCanceledException) when (!owned && !cancellationToken.IsCancellationRequested)
            {
                // The request we joined was cancelled by the caller that started it, not by us: start our own.
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            }
            catch (OperationCanceledException) when (!owned)
            {
                // We stopped waiting; the shared request carries on for the others.
                throw;
            }
            catch (IndexerUnavailableException) when (!owned)
            {
                // The governor refused the starter (its lane or deadline, e.g. "would wait past the search budget"):
                // that verdict is not ours, so ask for ourselves.
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            }
            catch
            {
                // Never cache failures.
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                throw;
            }
        }
    }

    /// <summary>Forgets one response (the indexer could not parse it: never serve it again).</summary>
    public void Remove(string key)
    {
        // Only a finished response: an entry still in flight belongs to a newer request (keep single-flight).
        if (_entries.TryGetValue(key, out var entry) && entry.Value.IsValueCreated && entry.Value.Value.IsCompleted)
        {
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
        }
    }

    public void Clear() => _entries.Clear();

    /// <summary>Drops expired, completed entries every few minutes: keys rarely repeat, so they would pile up (NS-08).</summary>
    private void SweepExpired(DateTimeOffset now)
    {
        if (now < _nextSweep)
        {
            return;
        }

        _nextSweep = now + SweepInterval;
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresUtc <= now && pair.Value.Value.IsValueCreated && pair.Value.Value.Value.IsCompleted)
            {
                _entries.TryRemove(pair);
            }
        }
    }

    private sealed record Entry(Lazy<Task<IndexerHttpResponse>> Value, DateTimeOffset ExpiresUtc);
}

/// <summary>
/// <see cref="IIndexerHttp"/> bound to one indexer and one lane: every request waits for a governor lease,
/// is classified (429 → rate limited, 403/challenge → blocked, 5xx/timeout → failure) and reported back.
/// Successful responses are cached. Uses .NET HttpClient, i.e. the Windows certificate store (the Python
/// plugins failed TLS on bitsearch/torlock with "certificate has expired" where Windows is fine).
/// </summary>
public sealed class GovernedIndexerHttp(
    HttpClient http,
    IndexerRequestGovernor governor,
    IndexerResponseCache cache,
    string indexerId,
    SearchLane lane,
    TimeSpan maxWait,
    TimeSpan cacheTtl,
    string userAgent,
    TimeSpan? requestTimeout = null) : IIndexerHttp
{
    private readonly object _pendingLock = new();
    private readonly List<string> _pendingKeys = [];
    private int _pendingNetworkResponses;

    public Task<IndexerHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken) =>
        GetCachedAsync($"{indexerId} GET {uri}", () => SendAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken), cancellationToken);

    public Task<IndexerHttpResponse> PostJsonAsync(Uri uri, string json, CancellationToken cancellationToken) =>
        GetCachedAsync($"{indexerId} POST {uri} {json}", () => SendAsync(() => new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }, cancellationToken), cancellationToken);

    /// <summary>
    /// Success is reported to the governor only once the indexer understood the answer; an answer it could not
    /// parse is reported as BadResponse and dropped from the cache. Cache hits report nothing: the site was not
    /// contacted (audit 07c NS-04).
    /// </summary>
    public void ReportParsed(bool parsed)
    {
        string[] keys;
        int networkResponses;
        lock (_pendingLock)
        {
            keys = [.. _pendingKeys];
            networkResponses = _pendingNetworkResponses;
            _pendingKeys.Clear();
            _pendingNetworkResponses = 0;
        }

        if (!parsed)
        {
            foreach (var key in keys)
            {
                cache.Remove(key);
            }
        }

        if (networkResponses > 0)
        {
            governor.Report(indexerId, parsed ? IndexerFailureKind.None : IndexerFailureKind.BadResponse, detail: parsed ? null : "response could not be parsed");
        }
    }

    private async Task<IndexerHttpResponse> GetCachedAsync(string key, Func<Task<IndexerHttpResponse>> send, CancellationToken cancellationToken)
    {
        var response = await cache.GetOrAddAsync(key, cacheTtl, send, cancellationToken, () =>
        {
            lock (_pendingLock)
            {
                _pendingNetworkResponses++;
            }
        });
        lock (_pendingLock)
        {
            _pendingKeys.Add(key);
        }

        return response;
    }

    private async Task<IndexerHttpResponse> SendAsync(Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        using var lease = await governor.AcquireAsync(indexerId, lane, maxWait, cancellationToken);
        using var request = build();
        request.Headers.UserAgent.ParseAdd(userAgent);
        HttpResponseMessage response;
        // The per-request timeout starts after the governor lease: waiting for our turn is not the site's fault.
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(15));
        try
        {
            response = await http.SendAsync(request, requestCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            governor.Report(indexerId, IndexerFailureKind.Timeout, detail: "HTTP timeout");
            throw new IndexerHttpException(indexerId, IndexerFailureKind.Timeout, "HTTP timeout");
        }
        catch (OperationCanceledException)
        {
            // Our own budget ran out: not the site's fault, nothing to report.
            throw;
        }
        catch (HttpRequestException ex)
        {
            governor.Report(indexerId, IndexerFailureKind.Network, detail: ex.Message);
            throw new IndexerHttpException(indexerId, IndexerFailureKind.Network, ex.Message);
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(requestCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                governor.Report(indexerId, IndexerFailureKind.Timeout, detail: "HTTP body timeout");
                throw new IndexerHttpException(indexerId, IndexerFailureKind.Timeout, "HTTP body timeout");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var status = (int)response.StatusCode;
            var finalUri = response.RequestMessage?.RequestUri ?? request.RequestUri!;

            // 429, or a 503 that says when to come back (doc 03: honour Retry-After for both).
            if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                (response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter is not null &&
                 !ChallengeDetector.LooksLikeChallenge(body, contentType)))
            {
                var retryAfter = response.Headers.RetryAfter?.Delta
                                 ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
                governor.Report(indexerId, IndexerFailureKind.RateLimited, retryAfter, $"HTTP {status}");
                throw new IndexerHttpException(indexerId, IndexerFailureKind.RateLimited, $"HTTP {status}", retryAfter);
            }

            if (status == 403 || ChallengeDetector.LooksLikeChallenge(body, contentType))
            {
                governor.Report(indexerId, IndexerFailureKind.Blocked, detail: $"HTTP {status} challenge/forbidden");
                throw new IndexerBlockedException(indexerId, $"HTTP {status}");
            }

            if (status >= 500)
            {
                governor.Report(indexerId, IndexerFailureKind.ServerError, detail: $"HTTP {status}");
                throw new IndexerHttpException(indexerId, IndexerFailureKind.ServerError, $"HTTP {status}");
            }

            if (status >= 400)
            {
                governor.Report(indexerId, IndexerFailureKind.BadResponse, detail: $"HTTP {status}");
                throw new IndexerHttpException(indexerId, IndexerFailureKind.BadResponse, $"HTTP {status}");
            }

            // Success is reported after parsing (ReportParsed), not here.
            return new IndexerHttpResponse(status, body, contentType, finalUri);
        }
    }
}

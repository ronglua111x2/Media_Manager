using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace media_management_app.Search;

/// <summary>Recognizes bot-challenge / block pages. A match means "stop calling this site", never "get around it".</summary>
public static class ChallengeDetector
{
    private static readonly Regex Markers = new(
        @"Just a moment\.\.\.|cf-chl|challenge-platform|<title>One moment|Making sure you&#39;re not a bot|Making sure you're not a bot|DDoS-Guard|Checking your browser|captcha",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool LooksLikeChallenge(string? body, string? contentType)
    {
        if (string.IsNullOrEmpty(body))
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
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IndexerHttpResponse> GetOrAddAsync(string key, TimeSpan ttl, Func<Task<IndexerHttpResponse>> factory)
    {
        var now = _time.GetUtcNow();
        while (true)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry(new Lazy<Task<IndexerHttpResponse>>(factory), now + ttl));
            if (entry.ExpiresUtc <= now && entry.Value.IsValueCreated && entry.Value.Value.IsCompleted)
            {
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                continue;
            }

            try
            {
                return await entry.Value.Value;
            }
            catch
            {
                // Never cache failures.
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                throw;
            }
        }
    }

    public void Clear() => _entries.Clear();

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
    string userAgent) : IIndexerHttp
{
    public Task<IndexerHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken) =>
        cache.GetOrAddAsync($"{indexerId} GET {uri}", cacheTtl, () => SendAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken));

    public Task<IndexerHttpResponse> PostJsonAsync(Uri uri, string json, CancellationToken cancellationToken) =>
        cache.GetOrAddAsync($"{indexerId} POST {uri} {json}", cacheTtl, () => SendAsync(() => new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }, cancellationToken));

    private async Task<IndexerHttpResponse> SendAsync(Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        using var lease = await governor.AcquireAsync(indexerId, lane, maxWait, cancellationToken);
        using var request = build();
        request.Headers.UserAgent.ParseAdd(userAgent);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
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
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var status = (int)response.StatusCode;
            var finalUri = response.RequestMessage?.RequestUri ?? request.RequestUri!;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta
                                 ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
                governor.Report(indexerId, IndexerFailureKind.RateLimited, retryAfter, "HTTP 429");
                throw new IndexerHttpException(indexerId, IndexerFailureKind.RateLimited, "HTTP 429", retryAfter);
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

            governor.Report(indexerId, IndexerFailureKind.None);
            return new IndexerHttpResponse(status, body, contentType, finalUri);
        }
    }
}

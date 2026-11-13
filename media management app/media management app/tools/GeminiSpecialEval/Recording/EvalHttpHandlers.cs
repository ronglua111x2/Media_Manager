using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeminiSpecialEval.Infrastructure;

namespace GeminiSpecialEval.Recording;

/// <summary>The eval's ledger refused the request; nothing was sent.</summary>
internal sealed class EvalBudgetExceededException : HttpRequestException
{
    public EvalBudgetExceededException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The only way out to the network. Lets through only <c>generateContent</c> for the one model this stack is
/// for, counts the request in the ledger before sending it, and records the raw response.
/// </summary>
internal sealed class LiveGuardHandler : DelegatingHandler
{
    private readonly QuotaLedger _ledger;
    private readonly string _model;

    /// <param name="inner">The real network (default), or an offline responder for a dry rehearsal of a run.</param>
    public LiveGuardHandler(QuotaLedger ledger, string model, HttpMessageHandler? inner = null)
        : base(inner ?? new HttpClientHandler())
    {
        _ledger = ledger;
        _model = Running.ModelSpec.IdOf(model);
    }

    /// <summary>Where the exchanges of the current sample go.</summary>
    public List<RecordedExchange>? Sink { get; set; }

    /// <summary>Ledger label for the current sample (case, prompt, sample number).</summary>
    public string Label { get; set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var expected = $"/models/{Uri.EscapeDataString(_model)}:generateContent";
        if (request.Method != HttpMethod.Post || request.RequestUri is null ||
            !request.RequestUri.AbsolutePath.EndsWith(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Eval guard: only POST …{expected} may be sent from this stack (got {request.Method} {request.RequestUri?.AbsolutePath}).");
        }

        var exchange = new RecordedExchange { StartedUtc = DateTimeOffset.UtcNow, ThinkingConfig = await RequestInspection.ThinkingConfigOf(request, cancellationToken) };
        if (!_ledger.TryReserve(_model, Label, out var entryId))
        {
            exchange.ErrorKind = "budget";
            exchange.Error = $"Eval quota cap reached for {_model} today; request not sent.";
            Sink?.Add(exchange);
            throw new EvalBudgetExceededException(exchange.Error);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            exchange.ElapsedMs = stopwatch.ElapsedMilliseconds;
            exchange.Status = (int)response.StatusCode;
            exchange.Body = body;
            Sink?.Add(exchange);
            _ledger.Complete(entryId, exchange.Status, null);

            // Hand the client a fresh copy of the body (the original stream was read above).
            var copy = new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
                ReasonPhrase = response.ReasonPhrase
            };
            response.Dispose();
            return copy;
        }
        catch (Exception ex)
        {
            exchange.ElapsedMs = stopwatch.ElapsedMilliseconds;
            exchange.ErrorKind = ex is OperationCanceledException ? "timeout" : "network";
            exchange.Error = SafeFile.Scrub(ex.Message);
            Sink?.Add(exchange);
            _ledger.Complete(entryId, null, exchange.ErrorKind);
            throw;
        }
    }
}

internal static class RequestInspection
{
    public static async Task<string?> ThinkingConfigOf(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return null;
        }

        var body = await request.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(body)?["generationConfig"]?["thinkingConfig"]?.ToJsonString();
    }
}

/// <summary>Answers from recorded exchanges, in order; never touches the network.</summary>
internal sealed class ReplayHandler : HttpMessageHandler
{
    private readonly Queue<RecordedExchange> _exchanges;

    public ReplayHandler(IEnumerable<RecordedExchange> exchanges) => _exchanges = new Queue<RecordedExchange>(exchanges);

    public int Served { get; private set; }

    public int Left => _exchanges.Count;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_exchanges.Count == 0)
        {
            throw new InvalidOperationException("Replay: the app sent more requests than were recorded.");
        }

        var exchange = _exchanges.Dequeue();
        Served++;
        if (exchange.Status is { } status)
        {
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(exchange.Body ?? string.Empty, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }

        throw exchange.ErrorKind switch
        {
            "timeout" => new TaskCanceledException("Replayed timeout."),
            "budget" => new EvalBudgetExceededException(exchange.Error ?? "Replayed budget refusal."),
            _ => new HttpRequestException(exchange.Error ?? "Replayed network failure.")
        };
    }
}

/// <summary>Answers each request with a function of the payload the app sent (self-test responders).</summary>
internal sealed class ResponderHandler : HttpMessageHandler
{
    private readonly Func<string, string> _answer;

    public ResponderHandler(Func<string, string> answerForPayload) => _answer = answerForPayload;

    public List<RecordedExchange> Exchanges { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        var payload = JsonNode.Parse(body)?["contents"]?[0]?["parts"]?[0]?["text"]?.GetValue<string>() ?? "{}";
        var responseBody = GeminiEnvelope(_answer(payload));
        var thinking = JsonNode.Parse(body)?["generationConfig"]?["thinkingConfig"]?.ToJsonString();
        Exchanges.Add(new RecordedExchange { StartedUtc = DateTimeOffset.UtcNow, Status = 200, ThinkingConfig = thinking, Body = responseBody });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
    }

    /// <summary>A generateContent response body carrying <paramref name="text"/> as the answer.</summary>
    public static string GeminiEnvelope(string text) =>
        "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + JsonSerializer.Serialize(text) +
        "}],\"role\":\"model\"},\"finishReason\":\"STOP\"}]}";
}

/// <summary>Rehearsal network: every <c>failEvery</c>-th request gets a 503, the others go to <c>inner</c>.</summary>
internal sealed class FlakyHandler : DelegatingHandler
{
    private readonly int _failEvery;
    private int _calls;

    public FlakyHandler(HttpMessageHandler inner, int failEvery)
        : base(inner)
    {
        _failEvery = failEvery;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _calls++;
        if (_calls % _failEvery == 0)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":503,\"message\":\"The model is overloaded (rehearsal).\",\"status\":\"UNAVAILABLE\"}}",
                    Encoding.UTF8,
                    "application/json"),
                RequestMessage = request
            });
        }

        return base.SendAsync(request, cancellationToken);
    }
}

using System.Text.Json;
using GeminiSpecialEval.Infrastructure;

namespace GeminiSpecialEval.Recording;

/// <summary>One HTTP exchange with Gemini as it happened (headers are never kept: the key travels in one).</summary>
internal sealed class RecordedExchange
{
    public DateTimeOffset StartedUtc { get; set; }

    public long ElapsedMs { get; set; }

    /// <summary>HTTP status, or null when no response came back (see <see cref="ErrorKind"/>).</summary>
    public int? Status { get; set; }

    /// <summary>timeout, network or budget (refused by the eval's ledger, never sent).</summary>
    public string? ErrorKind { get; set; }

    public string? Error { get; set; }

    /// <summary>The thinkingConfig of the request as sent (null = none), to check what the model was asked for.</summary>
    public string? ThinkingConfig { get; set; }

    /// <summary>The raw response body, exactly as Gemini sent it.</summary>
    public string? Body { get; set; }
}

internal static class SampleStatus
{
    /// <summary>Gemini answered and the answer parsed.</summary>
    public const string Ok = "ok";

    /// <summary>The request failed (HTTP error, timeout, unparseable answer); the app fell back to its rules.</summary>
    public const string Error = "error";

    /// <summary>A failed attempt that was retried; kept for the request count, not scored.</summary>
    public const string Retried = "retried";
}

/// <summary>
/// Everything about one request of the eval: which case, model and prompt, what was sent (hashes and payload),
/// every HTTP exchange, and what the app made of it. Enough to re-score it later with zero requests.
/// </summary>
internal sealed class SampleRecord
{
    public int Schema { get; set; } = 1;

    public string RunId { get; set; } = string.Empty;

    public string Case { get; set; } = string.Empty;

    public string CaseSource { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Variant { get; set; } = string.Empty;

    public int PromptVersion { get; set; }

    public string PromptSha256 { get; set; } = string.Empty;

    public string PayloadSha256 { get; set; } = string.Empty;

    public int Sample { get; set; }

    public int Attempt { get; set; } = 1;

    public DateTimeOffset StartedUtc { get; set; }

    public string PacificDay { get; set; } = string.Empty;

    public string Status { get; set; } = SampleStatus.Ok;

    /// <summary>The user payload sent (fictional for synthetic cases; private cases stay in the data folder).</summary>
    public string? Payload { get; set; }

    public List<RecordedExchange> Exchanges { get; set; } = [];

    /// <summary>The answer text the app's client extracted from the response.</summary>
    public string? ResponseText { get; set; }

    public string? AnsweredBy { get; set; }

    public string? FinishReason { get; set; }

    public string? Error { get; set; }

    /// <summary>Final S00 episode per special file after the app's checks, at record time (for the replay diff).</summary>
    public Dictionary<string, int?> FinalAtRecord { get; set; } = [];

    public string FileName =>
        $"{Sanitize(Case)}__{Sanitize(Model)}__{Variant}__s{Sample}{(Attempt > 1 || Status == SampleStatus.Retried ? $"_a{Attempt}" : string.Empty)}.json";

    public static string Sanitize(string value) =>
        string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '.' ? character : '_'));

    public void Save(string folder) =>
        SafeFile.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, Json.WriteOptions));

    public static SampleRecord Load(string path) =>
        JsonSerializer.Deserialize<SampleRecord>(File.ReadAllText(path), Json.ReadOptions)
        ?? throw new InvalidDataException($"Empty record {path}");
}

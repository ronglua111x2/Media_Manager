using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using media_management_app.Models;

namespace media_management_app.Services;

public static class SpecialMappingCompactJson
{
    private static readonly Regex MarkdownFenceRegex = new(
        @"^```(?:json)?\s*|\s*```$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonWriterOptions RequestWriterOptions = new()
    {
        // Show and episode titles are sent as they are (note R4): escaping every non-ASCII character as \uXXXX
        // made Japanese titles unreadable for the model and tripled their size.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Payload v2 (PR-14): only the files the rules left open, with the season air-date windows, the TMDB S00 list,
    /// the episodes the rules already fixed and the episodes still free. Rule proposals and review suggestions are
    /// never sent (Gemini copied them). Each file is sent as its file name plus the name of its own folder only.
    /// </summary>
    public static string SerializeRequest(SpecialMappingAIRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, RequestWriterOptions))
        {
            var context = request.Context;
            var fixedPairs = request.Proposals
                .Where(proposal => proposal.CandidateIndex >= 0 && proposal.CandidateIndex < context.Candidates.Count)
                .Select(proposal => (Episode: proposal.ProposedS00E, File: context.Candidates[proposal.CandidateIndex].FileName))
                .ToList();
            var fixedEpisodes = fixedPairs.Select(pair => pair.Episode).ToHashSet();

            writer.WriteStartObject();
            writer.WriteNumber("v", 2);
            writer.WritePropertyName("ctx");
            writer.WriteStartObject();
            writer.WriteString("t", context.ShowTitle);
            writer.WriteNumber("id", context.TmdbId);

            writer.WritePropertyName("sw");
            writer.WriteStartArray();
            foreach (var block in request.SeasonBlocks)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(block.SeasonNumber);
                writer.WriteStringValue(block.SeasonStart.ToString("yyyy-MM-dd"));
                writer.WriteStartArray();
                foreach (var entry in block.Entries)
                {
                    writer.WriteNumberValue(entry.EpisodeNumber);
                }

                writer.WriteEndArray();
                writer.WriteEndArray();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("c");
            writer.WriteStartArray();
            foreach (var index in request.GeminiCandidateIndices)
            {
                var candidate = context.Candidates[index];
                writer.WriteStartArray();
                writer.WriteStringValue(ParentFolderName(candidate.RelativePath));
                writer.WriteStringValue(candidate.FileName);
                writer.WriteNumberValue(candidate.ParentSeason ?? 0);
                writer.WriteNumberValue(candidate.LocalIndex);
                writer.WriteStringValue(ToPatternCode(candidate.NamingPattern));
                writer.WriteStringValue(string.Join(' ', SpecialEpisodeTitleMatcher.FileTitleTokens(candidate.FileName, context.ShowTitle)));
                writer.WriteEndArray();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("e");
            writer.WriteStartArray();
            foreach (var episode in context.TmdbSpecials)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(episode.Episode);
                writer.WriteStringValue(episode.AirDate);
                writer.WriteStringValue(episode.Title);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("fx");
            writer.WriteStartArray();
            foreach (var (episode, file) in fixedPairs.OrderBy(pair => pair.Episode))
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(episode);
                writer.WriteStringValue(file);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("free");
            writer.WriteStartArray();
            foreach (var episode in context.TmdbSpecials.Select(item => item.Episode).Where(number => !fixedEpisodes.Contains(number)).Order())
            {
                writer.WriteNumberValue(episode);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteString("task", request.Task);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ParentFolderName(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] : string.Empty;
    }

    public static SpecialMappingAIResponse ParseResponse(string json)
    {
        if (!TryParseResponse(json, out var response, out var error))
        {
            throw new InvalidOperationException(error ?? "Gemini response could not be parsed as JSON.");
        }

        return response!;
    }

    /// <summary>
    /// Reads <c>{"m":[[candidateIndex,s00e],...],"w":[...]}</c>. Never throws: a response without an
    /// <c>m</c> array is a failure, and a pair that is not two 32-bit integers is skipped with a warning.
    /// </summary>
    public static bool TryParseResponse(string raw, out SpecialMappingAIResponse? response, out string? error)
    {
        response = null;
        error = null;

        try
        {
            return TryParseResponseCore(raw, out response, out error);
        }
        catch (Exception ex)
        {
            response = null;
            error = $"Gemini response could not be parsed: {ex.Message}";
            return false;
        }
    }

    private static bool TryParseResponseCore(string raw, out SpecialMappingAIResponse? response, out string? error)
    {
        response = null;
        error = null;

        var candidates = GetResponseJsonCandidates(raw);
        if (candidates.Count == 0)
        {
            error = "Gemini response was empty.";
            return false;
        }

        Exception? lastException = null;
        string? lastCandidate = null;
        string? shapeError = null;
        foreach (var candidate in candidates)
        {
            lastCandidate = candidate;
            try
            {
                if (TryParseResponseDocument(candidate, out response, out var candidateShapeError))
                {
                    return true;
                }

                shapeError ??= candidateShapeError;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        if (lastException is null && shapeError is null && lastCandidate is not null)
        {
            try
            {
                using var _ = JsonDocument.Parse(lastCandidate);
            }
            catch (JsonException ex)
            {
                lastException = ex;
            }
        }

        var trimmed = TrimResponse(raw);
        if (TryRecoverMappingsOnly(trimmed, out response))
        {
            error = "Gemini warnings array was malformed; recovered mappings from m only.";
            return true;
        }

        response = null;
        error = shapeError
            ?? (lastException is not null && lastCandidate is not null
                ? BuildParseErrorMessage(lastException, lastCandidate)
                : "Gemini response could not be parsed as JSON.");
        return false;
    }

    private static bool TryParseResponseDocument(string normalized, out SpecialMappingAIResponse? response, out string? shapeError)
    {
        response = null;
        shapeError = null;
        using var document = JsonDocument.Parse(normalized);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            shapeError = $"Gemini response root is a JSON {root.ValueKind}, not an object.";
            return false;
        }

        if (!root.TryGetProperty("m", out var mappingElement) || mappingElement.ValueKind != JsonValueKind.Array)
        {
            shapeError = "Gemini response has no 'm' mapping array.";
            return false;
        }

        var mappings = new List<(int CandidateIndex, int S00E)>();
        var warnings = new List<string>();
        var position = 0;
        foreach (var item in mappingElement.EnumerateArray())
        {
            if (TryReadPair(item, out var candidateIndex, out var s00e))
            {
                mappings.Add((candidateIndex, s00e));
            }
            else
            {
                warnings.Add($"Ignored malformed Gemini mapping #{position + 1}: {Shorten(item.GetRawText())}.");
            }

            position++;
        }

        if (root.TryGetProperty("w", out var warningElement) && warningElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in warningElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    warnings.Add(item.GetString() ?? string.Empty);
                }
            }
        }

        response = new SpecialMappingAIResponse
        {
            Mappings = mappings,
            Warnings = warnings
        };
        return true;
    }

    private static bool TryReadPair(JsonElement item, out int candidateIndex, out int s00e)
    {
        candidateIndex = 0;
        s00e = 0;
        return item.ValueKind == JsonValueKind.Array &&
               item.GetArrayLength() >= 2 &&
               item[0].ValueKind == JsonValueKind.Number &&
               item[1].ValueKind == JsonValueKind.Number &&
               item[0].TryGetInt32(out candidateIndex) &&
               item[1].TryGetInt32(out s00e);
    }

    private static string Shorten(string value) =>
        value.Length <= 40 ? value : value[..40] + "...";

    private static List<string> GetResponseJsonCandidates(string raw)
    {
        var trimmed = TrimResponse(raw);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return [];
        }

        var candidates = new List<string>();
        void AddCandidate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!candidates.Contains(value, StringComparer.Ordinal))
            {
                candidates.Add(value);
            }
        }

        AddCandidate(ExtractFirstJsonValue(trimmed));
        AddCandidate(ExtractFirstJsonValue(trimmed, allowArrayRoot: true));

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            AddCandidate(trimmed[start..(end + 1)]);
        }

        AddCandidate(trimmed);
        return candidates;
    }

    private static string TrimResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return MarkdownFenceRegex.Replace(trimmed, string.Empty).Trim();
    }

    public static string NormalizeResponseJson(string raw)
    {
        var trimmed = TrimResponse(raw);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return ExtractFirstJsonValue(trimmed)
            ?? ExtractFirstJsonValue(trimmed, allowArrayRoot: true)
            ?? trimmed;
    }

    /// <summary>
    /// Extracts the first complete JSON object/array, ignoring any duplicated trailing fragments.
    /// </summary>
    public static string? ExtractFirstJsonValue(string raw, bool allowArrayRoot = false)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var start = -1;
        for (var i = 0; i < raw.Length; i++)
        {
            var ch = raw[i];
            if (ch == '{')
            {
                start = i;
                break;
            }

            if (allowArrayRoot && ch == '[')
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        var open = raw[start];
        var close = open == '[' ? ']' : '}';

        for (var i = start; i < raw.Length; i++)
        {
            var ch = raw[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == open)
            {
                depth++;
                continue;
            }

            if (ch == close)
            {
                depth--;
                if (depth == 0)
                {
                    return raw[start..(i + 1)];
                }
            }
        }

        return null;
    }

    public static string BuildResponseExcerpt(string raw, int bytePosition, int radius = 60)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "<empty>";
        }

        var start = Math.Max(0, bytePosition - radius);
        var length = Math.Min(raw.Length - start, radius * 2);
        if (length <= 0)
        {
            return raw.Length <= 240 ? raw : raw[..240] + "...";
        }

        var excerpt = raw.Substring(start, length);
        return start > 0 ? "..." + excerpt : excerpt;
    }

    private static bool TryRecoverMappingsOnly(string trimmed, out SpecialMappingAIResponse? response)
    {
        response = null;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var mappingsJson = ExtractPropertyArrayJson(trimmed, "m");
        if (mappingsJson is null)
        {
            return false;
        }

        var synthetic = $"{{\"m\":{mappingsJson},\"w\":[]}}";
        try
        {
            return TryParseResponseDocument(synthetic, out response, out _) && response!.Mappings.Count > 0;
        }
        catch (JsonException)
        {
            response = null;
            return false;
        }
    }

    private static string? ExtractPropertyArrayJson(string json, string propertyName)
    {
        var key = $"\"{propertyName}\"";
        var keyIndex = json.IndexOf(key, StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return null;
        }

        var colonIndex = json.IndexOf(':', keyIndex + key.Length);
        if (colonIndex < 0)
        {
            return null;
        }

        var start = colonIndex + 1;
        while (start < json.Length && char.IsWhiteSpace(json[start]))
        {
            start++;
        }

        if (start >= json.Length || json[start] != '[')
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < json.Length; i++)
        {
            var ch = json[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '[')
            {
                depth++;
                continue;
            }

            if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return json[start..(i + 1)];
                }
            }
        }

        return null;
    }

    private static string BuildParseErrorMessage(Exception ex, string normalized)
    {
        var position = ex is JsonException { BytePositionInLine: >= 0 } jsonException
            ? (int)jsonException.BytePositionInLine!.Value
            : 0;
        var excerpt = BuildResponseExcerpt(normalized, position);
        return $"Gemini JSON parse failed at byte {position}: {ex.Message}. Excerpt: {excerpt}";
    }

    private static string ToPatternCode(SpecialMappingNamingPattern pattern) => pattern switch
    {
        SpecialMappingNamingPattern.SnSnn => "s",
        SpecialMappingNamingPattern.OvaDash => "o",
        SpecialMappingNamingPattern.StandardS00E => "0",
        _ => "?"
    };
}

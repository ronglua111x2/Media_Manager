using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using media_management_app.Models;

namespace media_management_app.Services;

public static class SpecialMappingCompactJson
{
    private static readonly Regex MarkdownFenceRegex = new(
        @"^```(?:json)?\s*|\s*```$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string SerializeRequest(SpecialMappingAIRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("ctx");
            WriteContext(writer, request.Context);
            writer.WritePropertyName("p");
            writer.WriteStartArray();
            foreach (var proposal in request.Proposals)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(proposal.CandidateIndex);
                writer.WriteNumberValue(proposal.ProposedS00E);
                writer.WriteStringValue(ToReasonCode(proposal.Reason));
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteString("task", request.Task);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static SpecialMappingAIResponse ParseResponse(string json)
    {
        if (!TryParseResponse(json, out var response, out var error))
        {
            throw new InvalidOperationException(error ?? "Gemini response could not be parsed as JSON.");
        }

        return response!;
    }

    public static bool TryParseResponse(string raw, out SpecialMappingAIResponse? response, out string? error)
    {
        response = null;
        error = null;

        var candidates = GetResponseJsonCandidates(raw);
        if (candidates.Count == 0)
        {
            error = "Gemini response was empty.";
            return false;
        }

        JsonException? lastException = null;
        string? lastCandidate = null;
        foreach (var candidate in candidates)
        {
            lastCandidate = candidate;
            try
            {
                if (TryParseResponseDocument(candidate, out response))
                {
                    return true;
                }
            }
            catch (JsonException ex)
            {
                lastException = ex;
            }
        }

        if (lastException is null && lastCandidate is not null)
        {
            try
            {
                JsonDocument.Parse(lastCandidate);
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

        error = lastException is not null && lastCandidate is not null
            ? BuildParseErrorMessage(lastException, lastCandidate)
            : "Gemini response could not be parsed as JSON.";
        return false;
    }

    private static bool TryParseResponseDocument(string normalized, out SpecialMappingAIResponse? response)
    {
        response = null;
        using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;
            var mappings = new List<(int CandidateIndex, int S00E)>();
            var warnings = new List<string>();

            if (root.TryGetProperty("m", out var mappingElement) && mappingElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in mappingElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
                    {
                        continue;
                    }

                    mappings.Add((item[0].GetInt32(), item[1].GetInt32()));
                }
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
        return TryParseResponseDocument(synthetic, out response) && response!.Mappings.Count > 0;
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

    private static string BuildParseErrorMessage(JsonException ex, string normalized)
    {
        var position = ex.BytePositionInLine >= 0 ? (int)ex.BytePositionInLine : 0;
        var excerpt = BuildResponseExcerpt(normalized, position);
        return $"Gemini JSON parse failed at byte {position}: {ex.Message}. Excerpt: {excerpt}";
    }

    private static void WriteContext(Utf8JsonWriter writer, SpecialMappingContext context)
    {
        writer.WriteStartObject();
        writer.WriteString("t", context.ShowTitle);
        writer.WriteNumber("id", context.TmdbId);

        writer.WritePropertyName("blk");
        writer.WriteStartArray();
        foreach (var block in context.Blocks)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(block.ParentSeason);
            writer.WriteNumberValue(block.FileCount);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("c");
        writer.WriteStartArray();
        foreach (var candidate in context.Candidates)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(candidate.RelativePath);
            writer.WriteStringValue(candidate.FileName);
            writer.WriteNumberValue(candidate.ParentSeason ?? 0);
            writer.WriteNumberValue(candidate.LocalIndex);
            writer.WriteNumberValue(candidate.GlobalSortOrder);
            writer.WriteStringValue(ToPatternCode(candidate.NamingPattern));
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

        writer.WriteStartObject("x");
        writer.WriteNumber("nc", context.CandidateCount);
        writer.WriteNumber("nt", context.TmdbSpecialCount);
        writer.WriteBoolean("mm", context.CountMismatch);
        writer.WriteBoolean("op", context.HasOpaqueNames);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static string ToPatternCode(SpecialMappingNamingPattern pattern) => pattern switch
    {
        SpecialMappingNamingPattern.SnSnn => "s",
        SpecialMappingNamingPattern.OvaDash => "o",
        SpecialMappingNamingPattern.StandardS00E => "0",
        _ => "?"
    };

    private static string ToReasonCode(SpecialMappingProposalReason reason) => reason switch
    {
        SpecialMappingProposalReason.Direct => "direct",
        SpecialMappingProposalReason.Flatten => "flatten",
        SpecialMappingProposalReason.Title => "title",
        SpecialMappingProposalReason.Ordinal => "ordinal",
        _ => "none"
    };
}

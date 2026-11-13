using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using media_management_app.Services;

namespace GeminiSpecialEval.Prompts;

/// <summary>
/// A prompt + payload format the eval can send. The live variant is whatever the app sends today
/// (<see cref="GeminiSpecialMappingProvider.SpecialPromptVersion"/>): the provider's own prompt and payload pass
/// through untouched. Older variants are frozen copies here, and their payload is derived from the live payload,
/// so the app's runtime behaviour never changes for the eval.
/// </summary>
internal sealed class PromptVariant
{
    private readonly Func<string, string> _rewritePrompt;
    private readonly Func<string, string> _rewritePayload;

    private PromptVariant(string name, int version, bool isLive, Func<string, string> rewritePrompt, Func<string, string> rewritePayload)
    {
        Name = name;
        Version = version;
        IsLive = isLive;
        _rewritePrompt = rewritePrompt;
        _rewritePayload = rewritePayload;
    }

    public string Name { get; }

    public int Version { get; }

    /// <summary>True when this is the prompt the app sends today.</summary>
    public bool IsLive { get; }

    /// <summary>The system prompt to send, from the one the app's provider sends.</summary>
    public string RewritePrompt(string livePrompt) => _rewritePrompt(livePrompt);

    /// <summary>The user payload to send, from the one the app's provider sends.</summary>
    public string RewritePayload(string livePayload) => _rewritePayload(livePayload);

    public static int LiveVersion => GeminiSpecialMappingProvider.SpecialPromptVersion;

    public static string LiveName => $"v{LiveVersion}";

    public static IReadOnlyList<string> KnownNames => [LiveName, .. Frozen.Keys.Where(name => name != LiveName)];

    public static PromptVariant Get(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (normalized is "live" or "current" || normalized == LiveName)
        {
            return new PromptVariant(LiveName, LiveVersion, isLive: true, prompt => prompt, payload => payload);
        }

        if (Frozen.TryGetValue(normalized, out var factory))
        {
            return factory();
        }

        throw new ArgumentException(
            $"Unknown prompt variant '{name}'. Known: {string.Join(", ", KnownNames)}. " +
            "To compare against an older prompt, freeze it in PromptVariants.cs first.");
    }

    private static readonly Dictionary<string, Func<PromptVariant>> Frozen = new(StringComparer.Ordinal)
    {
        ["v2"] = () => new PromptVariant("v2", 2, isLive: false, MatchNewlines(V2SystemPrompt), PayloadV3ToV2)
    };

    /// <summary>The prompt is compiled from a CRLF source file; the frozen copy follows the live prompt's newlines.</summary>
    private static Func<string, string> MatchNewlines(string frozen) => livePrompt =>
    {
        var lf = frozen.Replace("\r\n", "\n");
        return livePrompt.Contains("\r\n", StringComparison.Ordinal) ? lf.Replace("\n", "\r\n") : lf;
    };

    /// <summary>
    /// Payload v2 from payload v3. The two formats differ only in <c>v</c> and in <c>ctx.fx</c>, whose entries v3
    /// extended from <c>[episode, fileName]</c> to <c>[episode, fileName, parentSeason, localIndex, patternCode]</c>
    /// (commit afdbfea, the only change to <c>SpecialMappingCompactJson.SerializeRequest</c>). Writes with the
    /// app's encoder, so titles stay as they are.
    /// </summary>
    public static string PayloadV3ToV2(string payloadV3)
    {
        var root = JsonNode.Parse(payloadV3)?.AsObject()
                   ?? throw new InvalidOperationException("Payload is not a JSON object.");
        var version = root["v"]?.GetValue<int>();
        if (version != 3)
        {
            throw new InvalidOperationException(
                $"The v2 payload is derived from payload v3, but the app now sends v{version}. Update PayloadV3ToV2.");
        }

        root["v"] = 2;
        if (root["ctx"]?["fx"] is JsonArray fixedPairs)
        {
            foreach (var pair in fixedPairs.OfType<JsonArray>())
            {
                while (pair.Count > 2)
                {
                    pair.RemoveAt(pair.Count - 1);
                }
            }
        }

        return root.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>The system prompt of version 2, verbatim from commit 92e6b58 (GeminiSpecialMappingProvider.cs).</summary>
    private const string V2SystemPrompt =
        """
        You map anime special/OVA files from a torrent to TMDB Season 0 (S00) episodes.

        Input JSON:
        ctx.t show title.
        ctx.sw season windows: [[season, seasonStartDate, [S00 episodes that aired during that season]]].
        ctx.c the files to map: [[folder, fileName, parentSeason, localIndex, patternCode, titleFromName]].
          candidateIndex is the 0-based position of a file in ctx.c.
          parentSeason 0 = unknown. localIndex = the number in the file name, 0 = none.
          patternCode: s = SnSnn (n-th special of season n), o = "OVA - N", 0 = S00Exx in the name, ? = no clear number.
        ctx.e TMDB S00 episodes: [[episode, airDate, title]].
        ctx.fx episodes already given to other files: [[episode, fileName]]. Never use these.
        ctx.free the only episode numbers you may use.

        Rules:
        - Map a file only when you are confident; leave it out otherwise.
        - Prefer the episodes in the window of the file's parentSeason.
        - Within one season keep the order of localIndex: a higher localIndex never gets a lower episode.
        - Use titleFromName against the TMDB titles when it has words.
        - Each candidateIndex and each episode at most once.

        OUTPUT (strict):
        - Return exactly one JSON object, no markdown fences, no prose before or after.
        - Shape: {"m":[[candidateIndex,s00e],...],"w":[]}
        - m: pairs of integers only (candidateIndex, TMDB S00 episode number). Never strings or titles.
        - w: usually []. Only short plain-ASCII warnings if needed (max 80 chars each, no double-quotes inside). Never copy titles, file names or paths.
        - No duplicate keys, no trailing commas, no second object.
        Example: {"m":[[0,9],[1,10]],"w":[]}
        """;
}

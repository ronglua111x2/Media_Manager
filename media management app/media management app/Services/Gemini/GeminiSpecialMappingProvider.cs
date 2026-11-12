using System.Text.Json;

using media_management_app.Common;

using media_management_app.Models;

using media_management_app.Services.Gemini;



namespace media_management_app.Services;



public sealed class GeminiSpecialMappingProvider

{

    private static readonly JsonElement SpecialMappingResponseSchema = JsonDocument.Parse(

        """

        {

          "type": "object",

          "properties": {

            "m": {

              "type": "array",

              "items": {

                "type": "array",

                "items": { "type": "integer" }

              }

            },

            "w": {

              "type": "array",

              "items": { "type": "string" }

            }

          },

          "required": ["m", "w"]

        }

        """).RootElement.Clone();



    /// <summary>Version of the prompt and payload format; part of the cache key (PR-15).</summary>
    public const int SpecialPromptVersion = 2;

    private const string SystemPrompt =
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

    private readonly IGeminiApiClient _geminiApiClient;

    private readonly ISettingsService _settingsService;

    private readonly IAppLogger _logger;



    public GeminiSpecialMappingProvider(

        IGeminiApiClient geminiApiClient,

        ISettingsService settingsService,

        IAppLogger logger)

    {

        _geminiApiClient = geminiApiClient;

        _settingsService = settingsService;

        _logger = logger;

    }



    public async Task<SpecialMappingResult> FinalizeAsync(

        SpecialMappingAIRequest request,

        IReadOnlyList<TrackedEpisode> specialsEpisodes,

        IProgress<PackLinkProgressUpdate>? progress = null,

        CancellationToken cancellationToken = default)

    {

        var userJson = SpecialMappingCompactJson.SerializeRequest(request);

        var gemini = _settingsService.Current.Gemini;
        var model = gemini?.AutoSelectModel ?? true
            ? "auto"
            : gemini?.Model?.Trim() ?? AppConstants.DefaultGeminiModel;



        _logger.Info(

            $"Gemini special mapping sending: model={model}, files={request.GeminiCandidateIndices.Count} of {request.Context.CandidateCount}, payloadChars={userJson.Length}.",

            LogTarget.All);



        PackLinkProgressReporter.Report(

            progress,

            PackLinkProgressStep.WaitingForGemini,

            PackLinkProgressStatus.Active,

            $"Calling Gemini ({model}) for {request.GeminiCandidateIndices.Count} file(s) the rules could not place...",

            $"Payload size: {userJson.Length} chars");



        var apiResult = await _geminiApiClient.GenerateJsonTextWithModelAsync(

            SystemPrompt,

            userJson,

            SpecialMappingResponseSchema,

            progress,

            cancellationToken);



        _logger.Info(

            $"Gemini special mapping response: model={apiResult.Model}, chars={apiResult.Json.Length}.",

            LogTarget.All);



        PackLinkProgressReporter.Report(

            progress,

            PackLinkProgressStep.WaitingForGemini,

            PackLinkProgressStatus.Done,

            $"Response received from {apiResult.Model} ({apiResult.Json.Length} chars).",

            apiResult.Json.Length <= 500 ? apiResult.Json : apiResult.Json[..500] + "...");



        PackLinkProgressReporter.Report(

            progress,

            PackLinkProgressStep.ParsingResponse,

            PackLinkProgressStatus.Active,

            "Parsing AI mappings...");



        if (!SpecialMappingCompactJson.TryParseResponse(apiResult.Json, out var parsed, out var parseError))

        {

            _logger.Error(

                $"Gemini special mapping JSON parse failed: {parseError}. Response excerpt: {SpecialMappingCompactJson.BuildResponseExcerpt(apiResult.Json, 0)}",

                null,

                LogTarget.All);

            PackLinkProgressReporter.Report(

                progress,

                PackLinkProgressStep.ParsingResponse,

                PackLinkProgressStatus.Failed,

                parseError ?? "Failed to parse Gemini response.",

                apiResult.Json);

            throw new InvalidOperationException(parseError ?? "Failed to parse Gemini response.");

        }



        _logger.Info(

            $"Gemini special mapping parsed: mappings={parsed!.Mappings.Count}, warnings={parsed.Warnings.Count}.",

            LogTarget.All);



        PackLinkProgressReporter.Report(

            progress,

            PackLinkProgressStep.ParsingResponse,

            PackLinkProgressStatus.Done,

            $"{parsed.Mappings.Count} mappings, {parsed.Warnings.Count} warning(s).");



        var extraWarnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(parseError))
        {
            extraWarnings.Add(parseError);
        }

        if (GeminiApiClient.IsEarlyStop(apiResult.FinishReason))
        {
            extraWarnings.Add(
                $"Gemini stopped before finishing (finishReason={apiResult.FinishReason}); some specials may be missing.");
        }

        return PackSpecialPayloadBuilder.BuildFromAiResponse(request, parsed, specialsEpisodes, extraWarnings);

    }

}



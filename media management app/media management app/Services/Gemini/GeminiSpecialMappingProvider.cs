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



    private const string SystemPrompt =

        """

        You finalize anime special/OVA torrent-to-TMDB mappings.

        Input JSON keys:

        ctx.t show title, ctx.id TMDB id, ctx.blk [[parentSeason,fileCount]], ctx.c [[path,fileName,parentSeason,localIndex,globalSort,patternCode]], ctx.e [[episode,airDate,title]], ctx.x stats.

        patternCode: s=SnSnn, o=OVA-dash, 0=S00E explicit, ?=opaque.

        p[] = [[candidateIndex, proposedS00E, reasonCode]] rule proposals to review.

        Rules: one candidateIndex per mapping, one s00e per mapping, prefer chronological TMDB order across season blocks, do not use title matching for patternCode s or ?.



        OUTPUT (strict):

        - Return exactly one JSON object, no markdown fences, no prose before or after.

        - Shape: {"m":[[candidateIndex,s00e],...],"w":[]}

        - m: pairs of integers only (candidateIndex, TMDB S00 episode number). Never strings or titles.

        - w: usually []. Only short plain-ASCII warnings if needed (max 80 chars each, no double-quotes inside). Never copy ctx.e episode titles, filenames, or paths.

        - No duplicate keys, no trailing commas, no second object.

        Example: {"m":[[0,3],[1,7]],"w":[]}

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

        var model = _settingsService.Current.Gemini?.Model?.Trim() ?? AppConstants.DefaultGeminiModel;



        _logger.Info(

            $"Gemini special mapping sending: model={model}, payloadChars={userJson.Length}.",

            LogTarget.All);



        PackLinkProgressReporter.Report(

            progress,

            PackLinkProgressStep.WaitingForGemini,

            PackLinkProgressStatus.Active,

            $"Calling Gemini ({model})...",

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



        return PackSpecialPayloadBuilder.BuildFromAiResponse(request, parsed, specialsEpisodes);

    }

}



using System.Net;
using System.Net.Http;
using System.Text;
using MediaManager.App.Tests.Link;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

/// <summary>The TMDB episode id is read for default seasons and for episode groups (audit L3-09).</summary>
public sealed class TmdbEpisodeIdParsingTests
{
    [Fact]
    public async Task GetTvShowDetails_ReadsEpisodeIds()
    {
        var provider = CreateProvider(new Dictionary<string, string>
        {
            ["tv/77"] = """{"name":"Demo Show","first_air_date":"2015-01-01","status":"Ended","seasons":[{"season_number":1,"episode_count":2}]}""",
            ["tv/77/season/1"] = """
                {"episodes":[
                  {"id":9001,"episode_number":1,"name":"One","air_date":"2015-01-01"},
                  {"id":9002,"episode_number":2,"name":"Two","air_date":"2015-01-08"}]}
                """
        });

        var details = await provider.GetTvShowDetailsAsync(77);

        var episodes = details.Seasons.Single().Episodes;
        Assert.Equal([(1, 9001), (2, 9002)], episodes.Select(item => (item.EpisodeNumber, item.TmdbEpisodeId!.Value)));
    }

    [Fact]
    public async Task GetTvShowDetailsByEpisodeGroup_ReadsBaseEpisodeIds()
    {
        var provider = CreateProvider(new Dictionary<string, string>
        {
            ["tv/77"] = """{"name":"Demo Show","first_air_date":"2015-01-01","status":"Ended"}""",
            ["tv/episode_group/grp"] = """
                {"group_count":1,"episode_count":2,"groups":[{"name":"Arc One","order":1,"episodes":[
                  {"id":9002,"order":0,"season_number":1,"episode_number":2,"name":"Two","air_date":"2015-01-08"},
                  {"id":9001,"order":1,"season_number":1,"episode_number":1,"name":"One","air_date":"2015-01-01"}]}]}
                """
        });

        var details = await provider.GetTvShowDetailsByEpisodeGroupAsync(77, "grp");

        var episodes = details.Seasons.Single().Episodes;
        Assert.Equal([(1, 9002), (2, 9001)], episodes.Select(item => (item.EpisodeNumber, item.TmdbEpisodeId!.Value)));
    }

    private static TmdbMetadataProvider CreateProvider(Dictionary<string, string> responses)
    {
        var settings = new AppSettings { TmdbReadAccessToken = "test-token" };
        return new TmdbMetadataProvider(
            TestFake<ISettingsService>.Create((method, _) => method.Name == "get_Current" ? settings : null),
            TestFake<IAppLogger>.Create(),
            new HttpClient(new FakeTmdbHandler(responses)),
            TestFake<ITmdbBlockAdvisor>.Create());
    }

    private sealed class FakeTmdbHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            path = path.StartsWith("3/", StringComparison.Ordinal) ? path[2..] : path;
            if (path.EndsWith("/alternative_titles", StringComparison.Ordinal))
            {
                return Task.FromResult(Json("""{"results":[]}"""));
            }

            return Task.FromResult(responses.TryGetValue(path, out var json)
                ? Json(json)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

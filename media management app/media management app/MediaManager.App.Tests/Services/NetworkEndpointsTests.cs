using media_management_app.Common;

namespace MediaManager.App.Tests.Services;

public class NetworkEndpointsTests
{
    [Theory]
    [InlineData(null, "api.tmdb.org")]
    [InlineData("", "api.tmdb.org")]
    [InlineData("  ", "api.tmdb.org")]
    [InlineData("api.themoviedb.org", "api.themoviedb.org")]
    [InlineData(" api.example.org ", "api.example.org")]
    [InlineData("https://api.tmdb.org", "api.tmdb.org")]
    [InlineData("api.tmdb.org/3", "api.tmdb.org")]
    [InlineData("localhost", "api.tmdb.org")]
    public void ResolveTmdbApiHost(string? configured, string expected)
    {
        Assert.Equal(expected, NetworkEndpoints.ResolveTmdbApiHost(configured));
    }

    [Fact]
    public void TmdbAuthenticationUri_UsesConfiguredHost()
    {
        Assert.Equal(
            "https://api.tmdb.org/3/authentication",
            NetworkEndpoints.TmdbAuthenticationUri(null).AbsoluteUri);
    }

    [Fact]
    public void DnsProbe_StaysOnTheHostUsedThroughTheTunnel()
    {
        Assert.Equal(NetworkEndpoints.TmdbLegacyApiHost, TmdbDnsProbe.ApiHost);
    }
}

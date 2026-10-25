using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using media_management_app.Common;

namespace MediaManager.App.Tests.Services;

public class TmdbNetworkBlockDetectorTests
{
    [Fact]
    public void ConnectionRefused_IsBlocked()
    {
        // Shape of the live error: "No connection could be made because the target machine actively refused it."
        var ex = new HttpRequestException(
            HttpRequestError.ConnectionError,
            "No connection could be made because the target machine actively refused it. (api.themoviedb.org:443)",
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(TmdbNetworkBlockDetector.IsLikelyBlocked(ex));
        Assert.Equal("ConnectionError", TmdbNetworkBlockDetector.Describe(ex));
    }

    [Fact]
    public void SocketResetWithoutHttpError_IsBlocked()
    {
        var ex = new HttpRequestException("send failed", new IOException("io", new SocketException((int)SocketError.ConnectionReset)));

        Assert.Equal("ConnectionReset", TmdbNetworkBlockDetector.Describe(ex));
    }

    [Fact]
    public void SslHandshakeFailure_IsBlocked()
    {
        var ex = new HttpRequestException(
            "The SSL connection could not be established, see inner exception.",
            new AuthenticationException("handshake"));

        Assert.Equal("SSL/TLS", TmdbNetworkBlockDetector.Describe(ex));
    }

    [Fact]
    public void HttpClientTimeout_IsBlocked()
    {
        var ex = new TaskCanceledException("timeout", new TimeoutException());

        Assert.True(TmdbNetworkBlockDetector.IsLikelyBlocked(ex, CancellationToken.None));
    }

    [Fact]
    public void UserCancellation_IsNotBlocked()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new TaskCanceledException("timeout", new TimeoutException());

        Assert.False(TmdbNetworkBlockDetector.IsLikelyBlocked(ex, cts.Token));
    }

    [Fact]
    public void HttpStatusError_IsNotBlocked()
    {
        var ex = new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);

        Assert.False(TmdbNetworkBlockDetector.IsLikelyBlocked(ex));
    }

    [Fact]
    public void JsonError_IsNotBlocked()
    {
        Assert.False(TmdbNetworkBlockDetector.IsLikelyBlocked(new JsonException("bad json")));
    }
}

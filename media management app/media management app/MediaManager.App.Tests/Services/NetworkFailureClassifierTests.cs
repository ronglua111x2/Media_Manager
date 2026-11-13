using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using media_management_app.Common;

namespace MediaManager.App.Tests.Services;

public class NetworkFailureClassifierTests
{
    private static HttpRequestException Http(HttpRequestError error, Exception? inner = null) =>
        new(error, "message", inner);

    private static HttpRequestException Status(HttpStatusCode status, string message = "message") =>
        new(message, null, status);

    private static IOException Socket(SocketError error) =>
        new("io", new SocketException((int)error));

    private static NetFailure Classify(Exception exception, bool online = true) =>
        NetworkFailureClassifier.Classify(exception, CancellationToken.None, () => online);

    public static TheoryData<Exception, NetFailure, bool> FallbackTable => new()
    {
        // Measured on FPT: loopback DNS answer, and TLS cut by SNI.
        { Http(HttpRequestError.ConnectionError, new SocketException((int)SocketError.ConnectionRefused)), NetFailure.ConnectRefused, true },
        { Http(HttpRequestError.SecureConnectionError, Socket(SocketError.ConnectionReset)), NetFailure.TlsFailure, true },
        { new HttpRequestException("ssl", new AuthenticationException("handshake")), NetFailure.TlsFailure, true },
        // Newly covered by the WARP fallback.
        { Http(HttpRequestError.ResponseEnded), NetFailure.ConnectionCut, true },
        { new HttpRequestException("send", Socket(SocketError.NetworkReset)), NetFailure.ConnectionCut, true },
        { new HttpRequestException("send", Socket(SocketError.ConnectionReset)), NetFailure.ConnectionCut, true },
        { Http(HttpRequestError.InvalidResponse), NetFailure.BadResponse, true },
        { Http(HttpRequestError.HttpProtocolError), NetFailure.BadResponse, true },
        { new HttpRequestException("dns", new SocketException((int)SocketError.TryAgain)), NetFailure.DnsFailure, true },
        { new HttpRequestException("dns", new SocketException((int)SocketError.NoData)), NetFailure.DnsFailure, true },
        { Http(HttpRequestError.NameResolutionError), NetFailure.DnsFailure, true },
        { new TaskCanceledException("timeout", new TimeoutException()), NetFailure.Timeout, true },
        { new OperationCanceledException("linked cts"), NetFailure.Timeout, true },
        { Status(HttpStatusCode.UnavailableForLegalReasons), NetFailure.BlockedResponse, true },
        { Status(HttpStatusCode.MovedPermanently), NetFailure.HostMoved, true },
        // Not fixed by a tunnel.
        { Status(HttpStatusCode.Unauthorized), NetFailure.AuthRejected, false },
        { Status(HttpStatusCode.Forbidden), NetFailure.AuthRejected, false },
        { Status(HttpStatusCode.Forbidden, "The SSL/TLS gateway said no"), NetFailure.AuthRejected, false },
        { Status(HttpStatusCode.TooManyRequests), NetFailure.RateLimited, false },
        { Status(HttpStatusCode.ServiceUnavailable), NetFailure.ServerError, false },
        { Status(HttpStatusCode.NotFound), NetFailure.ClientError, false },
        { new JsonException("bad json"), NetFailure.Unknown, false },
        { new InvalidOperationException("token missing"), NetFailure.Unknown, false }
    };

    [Theory]
    [MemberData(nameof(FallbackTable))]
    public void Classify_MatchesFallbackTable(Exception exception, NetFailure expected, bool warpFallback)
    {
        var failure = Classify(exception);

        Assert.Equal(expected, failure);
        Assert.Equal(warpFallback, NetworkFailureClassifier.IsWarpFallbackCandidate(failure));
    }

    [Fact]
    public void CallerCancellation_IsCancelled_AndNotAFallback()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var failure = NetworkFailureClassifier.Classify(new TaskCanceledException("x", new TimeoutException()), cts.Token);

        Assert.Equal(NetFailure.Cancelled, failure);
        Assert.False(NetworkFailureClassifier.IsWarpFallbackCandidate(failure));
    }

    [Theory]
    [InlineData(SocketError.NetworkDown)]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.NetworkUnreachable)]
    public void MachineOffline_DoesNotStartWarp(SocketError error)
    {
        var failure = Classify(new HttpRequestException("x", new SocketException((int)error)), online: false);

        Assert.Equal(NetFailure.Offline, failure);
        Assert.False(NetworkFailureClassifier.IsWarpFallbackCandidate(failure));
    }

    [Fact]
    public void HostNotFound_WhileOnline_IsDnsFailure()
    {
        var failure = Classify(new HttpRequestException("x", new SocketException((int)SocketError.HostNotFound)));

        Assert.Equal(NetFailure.DnsFailure, failure);
    }

    [Theory]
    [InlineData(NetFailure.ConnectRefused, true)]
    [InlineData(NetFailure.ConnectionCut, true)]
    [InlineData(NetFailure.TlsFailure, true)]
    [InlineData(NetFailure.DnsFailure, true)]
    [InlineData(NetFailure.BadResponse, true)]
    [InlineData(NetFailure.Timeout, false)]
    [InlineData(NetFailure.AuthRejected, false)]
    [InlineData(NetFailure.Offline, false)]
    public void IsRetryableTransportFailure(NetFailure failure, bool expected)
    {
        Assert.Equal(expected, NetworkFailureClassifier.IsRetryableTransportFailure(failure));
    }
}

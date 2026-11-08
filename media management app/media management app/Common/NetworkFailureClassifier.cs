using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Authentication;

namespace media_management_app.Common;

public enum NetFailure
{
    None,
    Cancelled,
    Timeout,
    Offline,
    DnsFailure,
    ConnectRefused,
    ConnectionCut,
    TlsFailure,
    BadResponse,
    BlockedResponse,
    HostMoved,
    AuthRejected,
    RateLimited,
    ServerError,
    ClientError,
    Unknown
}

/// <summary>
/// The one place that decides what a network failure means. An HTTP status beats everything else; after that
/// the exception type, <see cref="HttpRequestError"/> and <see cref="SocketError"/> decide. Exception messages
/// are localised and only ever used for display.
/// </summary>
public static class NetworkFailureClassifier
{
    public static NetFailure Classify(
        Exception? exception,
        CancellationToken callerToken = default,
        Func<bool>? isNetworkAvailable = null)
    {
        if (exception is null)
        {
            return NetFailure.None;
        }

        if (callerToken.IsCancellationRequested)
        {
            return NetFailure.Cancelled;
        }

        var chain = Chain(exception).ToList();

        // A cancellation the caller did not ask for is a timeout (HttpClient.Timeout or a linked CTS).
        if (chain.Any(item => item is OperationCanceledException or TimeoutException))
        {
            return NetFailure.Timeout;
        }

        if (chain.OfType<HttpRequestException>().FirstOrDefault(item => item.StatusCode is not null) is { } withStatus)
        {
            return Classify(withStatus.StatusCode!.Value);
        }

        var httpError = chain.OfType<HttpRequestException>().Select(item => item.HttpRequestError).FirstOrDefault();

        // Checked before sockets: a TLS handshake cut by RST also carries a SocketException.
        if (httpError == HttpRequestError.SecureConnectionError || chain.Any(item => item is AuthenticationException))
        {
            return NetFailure.TlsFailure;
        }

        if (chain.OfType<SocketException>().FirstOrDefault() is { } socket &&
            ClassifySocket(socket.SocketErrorCode, isNetworkAvailable) is { } socketFailure)
        {
            return socketFailure;
        }

        switch (httpError)
        {
            case HttpRequestError.NameResolutionError:
                return OfflineOr(NetFailure.DnsFailure, isNetworkAvailable);
            case HttpRequestError.ConnectionError:
                return OfflineOr(NetFailure.ConnectRefused, isNetworkAvailable);
            case HttpRequestError.ResponseEnded:
                return NetFailure.ConnectionCut;
            case HttpRequestError.InvalidResponse:
            case HttpRequestError.HttpProtocolError:
                return NetFailure.BadResponse;
        }

        return chain.Any(item => item is IOException) ? NetFailure.ConnectionCut : NetFailure.Unknown;
    }

    public static NetFailure Classify(HttpStatusCode status)
    {
        var code = (int)status;
        return code switch
        {
            >= 200 and < 300 => NetFailure.None,
            >= 300 and < 400 => NetFailure.HostMoved,
            401 or 403 => NetFailure.AuthRejected,
            429 => NetFailure.RateLimited,
            451 => NetFailure.BlockedResponse,
            >= 500 => NetFailure.ServerError,
            >= 400 => NetFailure.ClientError,
            _ => NetFailure.Unknown
        };
    }

    /// <summary>Failures a WARP tunnel can plausibly get past. Auth, rate limits and server errors cannot.</summary>
    public static bool IsWarpFallbackCandidate(NetFailure failure) =>
        failure is NetFailure.Timeout
            or NetFailure.DnsFailure
            or NetFailure.ConnectRefused
            or NetFailure.ConnectionCut
            or NetFailure.TlsFailure
            or NetFailure.BadResponse
            or NetFailure.BlockedResponse
            or NetFailure.HostMoved;

    /// <summary>Transport failures worth a quick retry on the same route.</summary>
    public static bool IsRetryableTransportFailure(NetFailure failure) =>
        failure is NetFailure.DnsFailure
            or NetFailure.ConnectRefused
            or NetFailure.ConnectionCut
            or NetFailure.TlsFailure
            or NetFailure.BadResponse;

    private static NetFailure? ClassifySocket(SocketError error, Func<bool>? isNetworkAvailable)
    {
        return error switch
        {
            SocketError.NetworkDown => NetFailure.Offline,
            SocketError.ConnectionRefused or SocketError.HostUnreachable => NetFailure.ConnectRefused,
            SocketError.NetworkUnreachable => OfflineOr(NetFailure.ConnectRefused, isNetworkAvailable),
            SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.NetworkReset => NetFailure.ConnectionCut,
            SocketError.TimedOut => NetFailure.Timeout,
            SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData or SocketError.NoRecovery
                => OfflineOr(NetFailure.DnsFailure, isNetworkAvailable),
            _ => null
        };
    }

    private static NetFailure OfflineOr(NetFailure failure, Func<bool>? isNetworkAvailable) =>
        (isNetworkAvailable ?? NetworkInterface.GetIsNetworkAvailable)() ? failure : NetFailure.Offline;

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}

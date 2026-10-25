using System.Net.Http;
using System.Net.Sockets;

namespace media_management_app.Common;

/// <summary>
/// Detects transport failures that look like TMDB blocking this network (refused/reset connection,
/// TLS handshake failure, DNS failure, timeout). Used only by Auto-Track WARP self-recover.
/// </summary>
public static class TmdbNetworkBlockDetector
{
    public static bool IsLikelyBlocked(Exception exception, CancellationToken cancellationToken = default)
    {
        return Describe(exception, cancellationToken) is not null;
    }

    /// <summary>Short reason label for logs, or null when the error is not a network block.</summary>
    public static string? Describe(Exception exception, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (SslTlsErrorDetector.IsSslOrTlsError(exception))
        {
            return "SSL/TLS";
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case SocketException socket when IsBlockingSocketError(socket.SocketErrorCode):
                    return socket.SocketErrorCode.ToString();
                case HttpRequestException http when http.StatusCode is null && IsBlockingHttpError(http.HttpRequestError):
                    return http.HttpRequestError.ToString();
                case TaskCanceledException { InnerException: TimeoutException }:
                    return "Timeout";
            }
        }

        return null;
    }

    private static bool IsBlockingSocketError(SocketError error) =>
        error is SocketError.ConnectionRefused
            or SocketError.ConnectionReset
            or SocketError.ConnectionAborted
            or SocketError.TimedOut
            or SocketError.HostUnreachable
            or SocketError.NetworkUnreachable
            or SocketError.HostNotFound;

    private static bool IsBlockingHttpError(HttpRequestError error) =>
        error is HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError
            or HttpRequestError.NameResolutionError;
}

using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace media_management_app.Services.Net;

/// <summary>Remembers the address of the last TMDB connection, so a failure log can say where it went.</summary>
public sealed class ConnectionTracker
{
    private string? _lastAddress;

    /// <summary>Peer address of the last connection, or what DNS answered when connecting failed.</summary>
    public string LastAddress => Volatile.Read(ref _lastAddress) ?? "?";

    public void Record(string address) => Volatile.Write(ref _lastAddress, address);
}

/// <summary>
/// Builds each long-lived HttpClient with the policy for its destination. Pooled connections expire so
/// nothing keeps using a socket opened on the old route after WARP changes it.
/// </summary>
public static class NetworkClients
{
    public static readonly TimeSpan TmdbRequestTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan SharedRequestTimeout = TimeSpan.FromSeconds(100);
    public static readonly TimeSpan JellyfinRequestTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan QbittorrentRequestTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ImageRequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ExternalConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LocalConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan QbittorrentConnectTimeout = TimeSpan.FromSeconds(5); // also resolves external torrent URLs
    private static readonly TimeSpan ExternalPooledLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LocalPooledLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Gemini, token tests and the Jellyfin pre-probe. Keeps the 100 s limit Gemini's own timeouts rely on.</summary>
    public static HttpClient CreateShared() =>
        new(new SocketsHttpHandler
        {
            ConnectTimeout = ExternalConnectTimeout,
            PooledConnectionLifetime = ExternalPooledLifetime
        })
        {
            Timeout = SharedRequestTimeout
        };

    /// <summary>Redirects are off: following one from the API host would land on a blocked host.</summary>
    public static HttpClient CreateTmdb(ConnectionTracker tracker)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = ExternalConnectTimeout,
            PooledConnectionLifetime = ExternalPooledLifetime,
            AllowAutoRedirect = false,
            ConnectCallback = (context, cancellationToken) => ConnectAndTrackAsync(context.DnsEndPoint, tracker, cancellationToken)
        };
        return new HttpClient(handler) { Timeout = TmdbRequestTimeout };
    }

    public static HttpClient CreateJellyfin() =>
        new(new SocketsHttpHandler
        {
            ConnectTimeout = LocalConnectTimeout,
            PooledConnectionLifetime = LocalPooledLifetime,
            UseProxy = false
        })
        {
            Timeout = JellyfinRequestTimeout
        };

    public static SocketsHttpHandler CreateQbittorrentHandler(CookieContainer cookies) =>
        new()
        {
            ConnectTimeout = QbittorrentConnectTimeout,
            PooledConnectionLifetime = LocalPooledLifetime,
            CookieContainer = cookies,
            UseCookies = true
        };

    public static HttpClient CreateImages() =>
        new(new SocketsHttpHandler
        {
            ConnectTimeout = ExternalConnectTimeout,
            PooledConnectionLifetime = ExternalPooledLifetime
        })
        {
            Timeout = ImageRequestTimeout
        };

    private static async ValueTask<Stream> ConnectAndTrackAsync(
        DnsEndPoint endPoint,
        ConnectionTracker tracker,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken);
            tracker.Record(socket.RemoteEndPoint is IPEndPoint remote ? MapAddress(remote.Address) : "?");
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            tracker.Record(await DescribeDnsAsync(endPoint.Host));
            throw;
        }
    }

    private static string MapAddress(IPAddress address) =>
        (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();

    private static async Task<string> DescribeDnsAsync(string host)
    {
        try
        {
            using var dnsCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var addresses = await Dns.GetHostAddressesAsync(host, dnsCts.Token);
            return addresses.Length == 0 ? "dns:none" : "dns:" + string.Join("|", addresses.Take(3).Select(MapAddress));
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return "dns:failed";
        }
    }
}

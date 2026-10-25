using System.Net;
using System.Net.Sockets;

namespace media_management_app.Common;

/// <summary>
/// The local network's DNS answers api.themoviedb.org with loopback (127.0.0.1 / ::1), which shows up as
/// "actively refused". After WARP connects, the OS resolver keeps that answer for a few seconds;
/// Auto-Track waits here until the host resolves to a routable address before retrying TMDB.
/// </summary>
public static class TmdbDnsProbe
{
    public const string ApiHost = "api.themoviedb.org";

    public static bool IsUsable(IReadOnlyCollection<IPAddress> addresses) =>
        addresses.Count > 0 &&
        addresses.Any(address => !IPAddress.IsLoopback(address) &&
                                 !address.Equals(IPAddress.Any) &&
                                 !address.Equals(IPAddress.IPv6Any));

    public static string Format(IReadOnlyCollection<IPAddress> addresses) =>
        addresses.Count == 0 ? "(none)" : string.Join(", ", addresses);

    /// <summary>
    /// Polls DNS until <see cref="ApiHost"/> resolves to a usable address or <paramref name="timeout"/> elapses.
    /// Returns the last addresses seen and whether they were usable.
    /// </summary>
    public static async Task<(bool Ready, IReadOnlyCollection<IPAddress> Addresses)> WaitUntilUsableAsync(
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        resolve ??= Dns.GetHostAddressesAsync;
        var deadline = DateTime.UtcNow + timeout;
        IReadOnlyCollection<IPAddress> last = [];
        while (true)
        {
            try
            {
                last = await resolve(ApiHost, cancellationToken);
            }
            catch (SocketException)
            {
                last = [];
            }

            if (IsUsable(last))
            {
                return (true, last);
            }

            if (DateTime.UtcNow + pollInterval > deadline)
            {
                return (false, last);
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }
}

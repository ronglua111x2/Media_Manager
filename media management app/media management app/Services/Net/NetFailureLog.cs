using media_management_app.Common;

namespace media_management_app.Services.Net;

public static class NetFailureLog
{
    /// <summary>One greppable line per failed request, with what is needed to tell a new block from an outage.</summary>
    public static string Format(
        string client,
        string host,
        string address,
        int attempt,
        int maxAttempts,
        NetFailure failure,
        TimeSpan elapsed,
        string warpState) =>
        $"net.fail client={client} host={host} ip={address} attempt={attempt}/{maxAttempts} class={failure} elapsed={elapsed.TotalMilliseconds:0}ms warp={warpState}";
}

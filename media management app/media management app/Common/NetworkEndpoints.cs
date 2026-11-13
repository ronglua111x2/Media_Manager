namespace media_management_app.Common;

/// <summary>
/// One table of the external hosts the app talks to. On the user's network <c>api.themoviedb.org</c> is blocked
/// (DNS answers loopback, TLS is cut by SNI) while <c>api.tmdb.org</c> and <c>image.tmdb.org</c> work, so the
/// API host is configurable (<c>TmdbApiHost</c>) and the blocked host is only used through a WARP tunnel.
/// </summary>
public static class NetworkEndpoints
{
    public const string TmdbApiHostDefault = "api.tmdb.org";

    /// <summary>
    /// Used through a tunnel by WARP recovery, and always by the Jellyfin pre-probe: Jellyfin's own TMDb plugin
    /// calls this host, so the probe must fail exactly when the plugin would.
    /// </summary>
    public const string TmdbLegacyApiHost = "api.themoviedb.org";

    public const string TmdbImageBaseUrl = "https://image.tmdb.org/t/p";
    public const string TmdbWebBaseUrl = "https://www.themoviedb.org";

    public static bool IsValidHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        Uri.CheckHostName(host.Trim()) == UriHostNameType.Dns &&
        host.Contains('.');

    /// <summary>The configured TMDB API host, or the default when it is missing or not a plain host name.</summary>
    public static string ResolveTmdbApiHost(string? configured) =>
        IsValidHost(configured) ? configured!.Trim() : TmdbApiHostDefault;

    public static Uri TmdbApiBase(string host) => new($"https://{host}/3/");

    public static Uri TmdbAuthenticationUri(string? configuredHost) =>
        new(TmdbApiBase(ResolveTmdbApiHost(configuredHost)), "authentication");
}

namespace media_management_app.Models;

public sealed class TorrentSearchResult
{
    private string fileUrl = string.Empty;
    private string descriptionUrl = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public long FileSize { get; init; }

    public string FileSizeDisplay => FormatSize(FileSize);

    public string FileUrl
    {
        get => fileUrl;
        init => fileUrl = NormalizeUrl(value);
    }

    public string DescriptionUrl
    {
        get => descriptionUrl;
        init => descriptionUrl = NormalizeUrl(value);
    }

    public bool CanAdd => IsDirectTorrentLink(FileUrl);

    public string LinkType => GetLinkType(FileUrl);

    public int Seeders { get; init; }

    public int Leechers { get; init; }

    public string EngineName { get; init; } = string.Empty;

    public string SiteUrl { get; init; } = string.Empty;

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    public static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _) ||
            trimmed.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("bc://bt/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(trimmed);
        }
        catch (UriFormatException)
        {
            return trimmed;
        }

        return Uri.TryCreate(decoded, UriKind.Absolute, out _) ||
               decoded.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) ||
               decoded.StartsWith("bc://bt/", StringComparison.OrdinalIgnoreCase)
            ? decoded
            : trimmed;
    }

    private static bool IsDirectTorrentLink(string url)
    {
        return GetLinkType(url) is "Magnet" or "Torrent URL" or "HTTP URL";
    }

    private static string GetLinkType(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Invalid";
        }

        if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            return "Magnet";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "Invalid";
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return "Unsupported";
        }

        return (uri.AbsolutePath + uri.Query).Contains(".torrent", StringComparison.OrdinalIgnoreCase)
            ? "Torrent URL"
            : "HTTP URL";
    }
}

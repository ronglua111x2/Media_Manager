namespace media_management_app.Common;

/// <summary>
/// Maps raw qBittorrent WebAPI torrent states to stable user-facing labels.
/// Transient API states like metaDL are folded into Downloading instead of being stored as-is.
/// </summary>
public static class QbittorrentTorrentStateNormalizer
{
    public const string Downloaded = "Downloaded";
    public const string Downloading = "Downloading";
    public const string Stalled = "Stalled";
    public const string Paused = "Paused";
    public const string Uploading = "Uploading";
    public const string Error = "Error";
    public const string MissingFiles = "MissingFiles";

    public static string Normalize(string? rawState, bool isComplete)
    {
        if (isComplete)
        {
            return Downloaded;
        }

        if (string.IsNullOrWhiteSpace(rawState))
        {
            return Downloading;
        }

        var state = rawState.Trim();
        if (string.Equals(state, Downloaded, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, Downloading, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, Stalled, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, Paused, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, Uploading, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, Error, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, MissingFiles, StringComparison.OrdinalIgnoreCase))
        {
            return CanonicalLabel(state);
        }

        return state.ToLowerInvariant() switch
        {
            "uploading" or "stalledup" or "queuedup" or "checkingup" or "forcedup" or "pausedup" => Uploading,
            "downloading" or "forceddl" or "queueddl" or "checkingdl" or "metadl" or "allocating"
                or "checkingresumedata" or "moving" => Downloading,
            "stalleddl" => Stalled,
            "pauseddl" => Paused,
            "error" => Error,
            "missingfiles" => MissingFiles,
            // Unknown / transient API noise — never persist raw values like metaDL.
            _ => Downloading
        };
    }

    private static string CanonicalLabel(string state) => state.ToLowerInvariant() switch
    {
        "downloaded" => Downloaded,
        "downloading" => Downloading,
        "stalled" => Stalled,
        "paused" => Paused,
        "uploading" => Uploading,
        "error" => Error,
        "missingfiles" => MissingFiles,
        _ => Downloading
    };
}

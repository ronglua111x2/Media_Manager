namespace media_management_app.Models;

/// <summary>
/// Result of validating torrent content for malware/suspicious files.
/// </summary>
public sealed class TorrentContentValidationResult
{
    /// <summary>
    /// True if torrent content is safe, false if malicious/suspicious files detected.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Torrent hash being validated.
    /// </summary>
    public required string TorrentHash { get; set; }

    /// <summary>
    /// List of suspicious files found in torrent.
    /// </summary>
    public List<SuspiciousFile> SuspiciousFiles { get; set; } = [];

    /// <summary>
    /// Validation errors or detailed reasons why torrent is invalid.
    /// </summary>
    public List<string> ValidationErrors { get; set; } = [];

    /// <summary>
    /// Recommendation for handling (delete, quarantine, etc).
    /// </summary>
    public TorrentHandleRecommendation Recommendation { get; set; }

    /// <summary>
    /// Timestamp of validation.
    /// </summary>
    public DateTime ValidatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Summary message for logging.
    /// </summary>
    public string Summary
    {
        get
        {
            if (IsValid)
                return "Torrent content validation passed.";

            return $"Torrent validation FAILED: {SuspiciousFiles.Count} suspicious file(s), {ValidationErrors.Count} error(s). Recommendation: {Recommendation}";
        }
    }
}

/// <summary>
/// Information about a suspicious file found in torrent.
/// </summary>
public sealed class SuspiciousFile
{
    /// <summary>
    /// File name.
    /// </summary>
    public required string FileName { get; set; }

    /// <summary>
    /// File extension (e.g., ".exe", ".scr").
    /// </summary>
    public required string Extension { get; set; }

    /// <summary>
    /// Reason why file is considered suspicious.
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Severity level of suspicion.
    /// </summary>
    public SuspicionLevel SuspicionLevel { get; set; }
}

/// <summary>
/// Recommendation for handling malicious/suspicious torrent.
/// </summary>
public enum TorrentHandleRecommendation
{
    /// <summary>
    /// Torrent is safe, proceed normally.
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Delete torrent from qBittorrent and blacklist.
    /// </summary>
    Delete = 1,

    /// <summary>
    /// Quarantine until manual review.
    /// </summary>
    Quarantine = 2,

    /// <summary>
    /// Unable to validate, needs investigation.
    /// </summary>
    ReviewRequired = 3
}

/// <summary>
/// Severity level of file suspicion.
/// </summary>
public enum SuspicionLevel
{
    /// <summary>
    /// Likely malicious (executable).
    /// </summary>
    Critical = 0,

    /// <summary>
    /// Suspicious but not clearly malicious.
    /// </summary>
    High = 1,

    /// <summary>
    /// Medium suspicion.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Minor concern.
    /// </summary>
    Low = 3
}

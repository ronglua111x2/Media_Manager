using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Validates torrent file lists for dangerous extensions, double-extension obfuscation,
/// and main-payload format mismatch. Extra files (.nfo, images, subs) are ignored.
/// </summary>
public sealed class TorrentContentValidationService
{
    private static readonly HashSet<string> PayloadIgnoreExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".nfo", ".txt", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
        ".srt", ".ass", ".ssa", ".idx", ".sub", ".sup", ".sfv", ".md5",
        ".url", ".m3u", ".cue"
    };

    /// <summary>
    /// Always dangerous, whatever the settings say (cross-review 2, user-approved): Windows shortcuts and script or
    /// installer launchers run code when opened. settings.json files saved the DangerousExtensions list (without
    /// .lnk), so changing the config default alone would not reach existing installs. ".url" (a web link) stays an
    /// ignored extra.
    /// SEC-A04: disk images (mount on double-click, contents lose Mark-of-the-Web), Windows search/library/settings
    /// shortcuts and .website (NTLM leak or command run when Explorer shows them), ClickOnce, Excel add-ins,
    /// script components, troubleshooter packs and installer patches. None belongs in a film or episode torrent.
    /// </summary>
    internal static readonly IReadOnlyList<string> AlwaysDangerousExtensions =
    [
        ".lnk", ".hta", ".wsf", ".wsh", ".vbe", ".jse", ".msc", ".reg", ".scf", ".chm", ".msix", ".appx", ".appinstaller",
        ".iso", ".img", ".vhd", ".vhdx",
        ".library-ms", ".searchconnector-ms", ".settingcontent-ms", ".website",
        ".application", ".appref-ms", ".xll", ".wsc", ".sct", ".diagcab", ".msp"
    ];

    private const int PackPayloadCount = 3;

    private readonly Func<TorrentValidationConfig> _getConfig;
    private readonly ITorrentContentValidationLogger? _logger;
    private int _warnedDisabled;
    private int _warnedWeakList;

    public TorrentContentValidationService(
        Func<TorrentValidationConfig>? getConfig = null,
        ITorrentContentValidationLogger? logger = null)
    {
        _getConfig = getConfig ?? (() => new TorrentValidationConfig());
        _logger = logger;
    }

    public Task<TorrentContentValidationResult> ValidateFilesAsync(
        string torrentHash,
        IReadOnlyList<TorrentContentFile> files,
        CancellationToken cancellationToken = default,
        string? listingName = null,
        bool isPack = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new TorrentContentValidationResult
        {
            TorrentHash = torrentHash,
            IsValid = true,
            Recommendation = TorrentHandleRecommendation.Safe
        };

        var config = _getConfig() ?? new TorrentValidationConfig();
        if (!config.EnableContentValidation)
        {
            if (Interlocked.Exchange(ref _warnedDisabled, 1) == 0)
            {
                _logger?.Warning(
                    "Torrent content validation is disabled in settings.json (TorrentValidation.EnableContentValidation = false): " +
                    "torrents are added and adopted without checking their files for executables or shortcuts.");
            }

            return Task.FromResult(result);
        }

        WarnIfSavedListDropsBuiltIns(config);

        if (files.Count == 0)
        {
            // Metadata not ready — caller decides whether to wait or skip. Not malware.
            result.ValidationErrors.Add("Torrent file list is empty (metadata may still be downloading).");
            result.Recommendation = TorrentHandleRecommendation.ReviewRequired;
            return Task.FromResult(result);
        }

        foreach (var file in files)
        {
            ValidateFile(file, result, config);
        }

        ValidatePayloadMismatch(files, listingName, isPack, result, config);

        if (!result.IsValid)
        {
            result.Recommendation = TorrentHandleRecommendation.Delete;
            _logger?.Info($"Torrent validation for {torrentHash}: {result.Summary}");
        }
        else
        {
            _logger?.Debug($"Torrent validation for {torrentHash}: {result.Summary}");
        }

        return Task.FromResult(result);
    }

    private void ValidateFile(
        TorrentContentFile file,
        TorrentContentValidationResult result,
        TorrentValidationConfig config)
    {
        var fileName = Path.GetFileName(file.Name);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var dangerous = GetDangerousExtensions(config);

        if (dangerous.Contains(extension))
        {
            result.SuspiciousFiles.Add(new SuspiciousFile
            {
                FileName = fileName,
                Extension = extension,
                Reason = $"Dangerous extension {extension} detected",
                FileSizeBytes = file.Size,
                SuspicionLevel = SuspicionLevel.Critical
            });
            result.IsValid = false;
            _logger?.Warning(
                $"Critical malware indicator: {extension} file '{fileName}' in torrent {result.TorrentHash}");
        }

        if (config.CheckExtensionObfuscation && IsLikelyObfuscated(fileName, extension, config, dangerous))
        {
            result.SuspiciousFiles.Add(new SuspiciousFile
            {
                FileName = fileName,
                Extension = extension,
                Reason = "Possible extension obfuscation detected",
                FileSizeBytes = file.Size,
                SuspicionLevel = SuspicionLevel.High
            });
            result.IsValid = false;
            _logger?.Warning(
                $"Possible obfuscation: '{fileName}' in torrent {result.TorrentHash}");
        }
    }

    private void ValidatePayloadMismatch(
        IReadOnlyList<TorrentContentFile> files,
        string? listingName,
        bool isPack,
        TorrentContentValidationResult result,
        TorrentValidationConfig config)
    {
        var mediaExts = config.AllowedMediaExtensions
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var payloads = SelectPayloadFiles(files, listingName, isPack);
        foreach (var payload in payloads)
        {
            var fileName = Path.GetFileName(payload.Name);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (mediaExts.Contains(extension))
            {
                continue;
            }

            if (result.SuspiciousFiles.Any(item =>
                    string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.SuspiciousFiles.Add(new SuspiciousFile
            {
                FileName = fileName,
                Extension = extension,
                Reason = $"Main payload is {extension}, not an allowed media format",
                FileSizeBytes = payload.Size,
                SuspicionLevel = SuspicionLevel.Critical
            });
            result.IsValid = false;
            _logger?.Warning(
                $"Payload mismatch: '{fileName}' ext={extension} listing='{listingName}' torrent={result.TorrentHash}");
            _logger?.Debug(
                $"Payload mismatch details: path='{payload.Name}', size={payload.Size}, pack={isPack}.");
        }
    }

    private static IReadOnlyList<TorrentContentFile> SelectPayloadFiles(
        IReadOnlyList<TorrentContentFile> files,
        string? listingName,
        bool isPack)
    {
        var candidates = files
            .Where(file => !IsIgnoredExtra(file.Name))
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = files.ToList();
        }

        var ranked = candidates
            .Select(file => (
                File: file,
                Similarity: NameSimilarity(Path.GetFileNameWithoutExtension(file.Name), listingName)))
            .OrderByDescending(item => item.Similarity)
            .ThenByDescending(item => item.File.Size)
            .Select(item => item.File)
            .ToList();

        var take = isPack ? Math.Min(PackPayloadCount, ranked.Count) : Math.Min(1, ranked.Count);
        return ranked.Take(take).ToList();
    }

    private static bool IsIgnoredExtra(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && PayloadIgnoreExtensions.Contains(extension);
    }

    private static double NameSimilarity(string fileName, string? listingName)
    {
        var fileTokens = Tokenize(fileName);
        var listingTokens = Tokenize(listingName);
        if (fileTokens.Count == 0 || listingTokens.Count == 0)
        {
            return 0;
        }

        var intersect = fileTokens.Intersect(listingTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = fileTokens.Union(listingTokens, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : (double)intersect / union;
    }

    private static HashSet<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new System.Text.StringBuilder();
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(char.ToLowerInvariant(c));
                continue;
            }

            FlushToken(current, tokens);
        }

        FlushToken(current, tokens);
        return tokens;
    }

    private static void FlushToken(System.Text.StringBuilder current, HashSet<string> tokens)
    {
        if (current.Length == 0)
        {
            return;
        }

        var token = current.ToString();
        current.Clear();
        if (token.Length >= 2)
        {
            tokens.Add(token);
        }
    }

    private void WarnIfSavedListDropsBuiltIns(TorrentValidationConfig config)
    {
        if (Volatile.Read(ref _warnedWeakList) != 0)
        {
            return;
        }

        var saved = (config.DangerousExtensions ?? [])
            .Select(NormalizeExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = TorrentValidationConfig.BuiltInDangerousExtensions
            .Select(NormalizeExtension)
            .Where(extension => !saved.Contains(extension))
            .ToList();
        if (missing.Count == 0 || Interlocked.Exchange(ref _warnedWeakList, 1) != 0)
        {
            return;
        }

        _logger?.Warning(
            $"settings.json TorrentValidation.DangerousExtensions leaves out {missing.Count} built-in entr{(missing.Count == 1 ? "y" : "ies")} " +
            $"({string.Join(", ", missing.Take(12))}{(missing.Count > 12 ? ", ..." : string.Empty)}). They are still blocked; the setting can only add extensions.");
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = (extension ?? string.Empty).Trim().ToLowerInvariant();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    private static HashSet<string> GetDangerousExtensions(TorrentValidationConfig config)
    {
        return config.GetAllDangerousExtensions()
            .Concat(TorrentValidationConfig.BuiltInDangerousExtensions)
            .Concat(AlwaysDangerousExtensions)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsLikelyObfuscated(
        string fileName,
        string detectedExtension,
        TorrentValidationConfig config,
        HashSet<string> dangerousExts)
    {
        if (fileName.Count(c => c == '.') < 2)
        {
            return false;
        }

        var withoutLast = Path.GetFileNameWithoutExtension(fileName);
        var secondExtension = Path.GetExtension(withoutLast).ToLowerInvariant();
        var mediaExts = config.AllowedMediaExtensions
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return mediaExts.Contains(secondExtension) && dangerousExts.Contains(detectedExtension);
    }
}

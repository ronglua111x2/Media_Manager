namespace media_management_app.Services;

public sealed class SeasonPackCleanupScope
{
    public required int OwnerSeasonNumber { get; init; }

    public required IReadOnlyList<int> CoveredSeasonNumbers { get; init; }

    public required IReadOnlyList<string> TorrentHashes { get; init; }

    public string CoveredSeasonsDisplay => FormatSeasonList(CoveredSeasonNumbers);

    public static string FormatSeasonList(IReadOnlyList<int> seasons) =>
        seasons.Count == 0
            ? string.Empty
            : string.Join(", ", seasons.Select(season => $"S{season:00}"));
}

public sealed record SeasonPackCleanupCandidate
{
    public long Id { get; init; }

    public bool IsExternalImport { get; init; }

    public bool IsOrphanPackSpecial { get; init; }

    public int? PackOwnerSeasonNumber { get; init; }

    public string? TorrentHash { get; init; }

    public bool IsSeasonPackLink { get; init; }

    public string FilePath { get; init; } = string.Empty;
}

public static class SeasonPackCleanupMatcher
{
    public static SeasonPackCleanupScope Resolve(
        int ownerSeasonNumber,
        string? coveredSeasonsValue,
        string? packTorrentHash,
        string? lastPackLinkTorrentHash)
    {
        var covered = ParseCoveredSeasons(coveredSeasonsValue);
        if (covered.Count == 0)
        {
            covered.Add(ownerSeasonNumber);
        }
        else if (!covered.Contains(ownerSeasonNumber))
        {
            covered.Add(ownerSeasonNumber);
            covered.Sort();
        }

        var hashes = new List<string>();
        AddHash(hashes, packTorrentHash);
        AddHash(hashes, lastPackLinkTorrentHash);

        return new SeasonPackCleanupScope
        {
            OwnerSeasonNumber = ownerSeasonNumber,
            CoveredSeasonNumbers = covered,
            TorrentHashes = hashes
        };
    }

    public static IReadOnlyList<SeasonPackCleanupCandidate> SelectItems(
        IEnumerable<SeasonPackCleanupCandidate> items,
        SeasonPackCleanupScope scope,
        IReadOnlySet<string> packSourcePaths)
    {
        return items
            .Where(item => MatchesProvenance(item, scope) || MatchesLegacyExactPath(item, scope, packSourcePaths))
            .ToList();
    }

    public static bool MatchesProvenance(SeasonPackCleanupCandidate item, SeasonPackCleanupScope scope)
    {
        if (item.IsExternalImport)
        {
            return false;
        }

        if (item.PackOwnerSeasonNumber == scope.OwnerSeasonNumber)
        {
            return true;
        }

        return IsPackAssociated(item) && HashMatches(item.TorrentHash, scope.TorrentHashes);
    }

    public static bool MatchesLegacyExactPath(
        SeasonPackCleanupCandidate item,
        SeasonPackCleanupScope scope,
        IReadOnlySet<string> packSourcePaths)
    {
        if (item.IsExternalImport ||
            packSourcePaths.Count == 0 ||
            scope.TorrentHashes.Count == 0)
        {
            return false;
        }

        if (item.PackOwnerSeasonNumber is not null || item.IsSeasonPackLink)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.FilePath))
        {
            return false;
        }

        return packSourcePaths.Contains(NormalizePath(item.FilePath));
    }

    public static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public static List<int> ParseCoveredSeasons(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(item, out var season) ? season : 0)
            .Where(season => season > 0)
            .Distinct()
            .ToList();
    }

    private static bool IsPackAssociated(SeasonPackCleanupCandidate item) =>
        item.IsSeasonPackLink || item.IsOrphanPackSpecial;

    private static bool HashMatches(string? hash, IReadOnlyList<string> packHashes)
    {
        if (string.IsNullOrWhiteSpace(hash) || packHashes.Count == 0)
        {
            return false;
        }

        return packHashes.Any(candidate => string.Equals(candidate, hash, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddHash(List<string> hashes, string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return;
        }

        if (hashes.Any(existing => string.Equals(existing, hash, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        hashes.Add(hash);
    }
}

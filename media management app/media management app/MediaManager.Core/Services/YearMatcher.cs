namespace media_management_app.Services;

public enum YearMatchKind
{
    /// <summary>The movie has no known year, so the check does not apply.</summary>
    NotApplicable,

    /// <summary>The release name carries the expected year.</summary>
    Exact,

    /// <summary>The release name carries a year one off (festival vs theatrical release, TMDB vs scene).</summary>
    OffByOne,

    /// <summary>The release name carries no release year at all.</summary>
    Missing,

    /// <summary>The release name only carries years two or more away: most likely another film or a remake.</summary>
    Mismatch
}

public readonly record struct YearMatch(YearMatchKind Kind, int? FoundYear, string Detail)
{
    public bool IsRejected => Kind == YearMatchKind.Mismatch;
}

/// <summary>
/// Release-year check for movies (native-search P1, SRCH-11). Replaces the old
/// <c>FileName.Contains(ReleaseYear)</c> test, which rejected ~97% of "Coherence" releases
/// (TMDB 2014, release names 2013) and could match a year hidden inside other numbers.
/// Years are read as standalone 4-digit tokens; numbers that belong to the title itself
/// ("2001: A Space Odyssey", "1917", "Blade Runner 2049") are not treated as the release year.
/// </summary>
public static class YearMatcher
{
    private const int MinYear = 1900;

    public static IReadOnlyList<int> ReleaseYears(string? releaseName, IEnumerable<string>? expectedTitles = null, int? maxYear = null)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return [];
        }

        var upper = maxYear ?? DateTime.UtcNow.Year + 1;
        var titleNumbers = new HashSet<string>(
            (expectedTitles ?? []).SelectMany(title => TorrentCandidateParser.Tokenize(title ?? string.Empty))
                .Where(token => token.Length == 4 && token.All(char.IsDigit)),
            StringComparer.Ordinal);

        var years = new List<int>();
        foreach (var token in TorrentCandidateParser.Tokenize(releaseName))
        {
            if (token.Length != 4 || !token.All(char.IsDigit) || !int.TryParse(token, out var year))
            {
                continue;
            }

            if (year < MinYear || year > upper)
            {
                continue;
            }

            // A number that is part of the title ("1917", "2049") is consumed once by the title.
            if (titleNumbers.Remove(token))
            {
                continue;
            }

            years.Add(year);
        }

        return years;
    }

    public static YearMatch Match(int? expectedYear, string? releaseName, IEnumerable<string>? expectedTitles = null, int? maxYear = null)
    {
        if (expectedYear is null)
        {
            return new YearMatch(YearMatchKind.NotApplicable, null, "no expected year");
        }

        var years = ReleaseYears(releaseName, expectedTitles, maxYear);
        if (years.Count == 0)
        {
            return new YearMatch(YearMatchKind.Missing, null, "no release year in name");
        }

        if (years.Contains(expectedYear.Value))
        {
            return new YearMatch(YearMatchKind.Exact, expectedYear, $"release year {expectedYear}");
        }

        var nearest = years.OrderBy(year => Math.Abs(year - expectedYear.Value)).First();
        return Math.Abs(nearest - expectedYear.Value) == 1
            ? new YearMatch(YearMatchKind.OffByOne, nearest, $"release year {nearest} is one off {expectedYear}")
            : new YearMatch(YearMatchKind.Mismatch, nearest, $"release year {nearest} differs from {expectedYear}");
    }

    /// <summary>Identity bonus that ranks exact year above one-off above year-less releases.</summary>
    public static int IdentityBonus(YearMatchKind kind) => kind switch
    {
        YearMatchKind.Exact => 2,
        YearMatchKind.OffByOne => 1,
        _ => 0
    };
}

using System.Text.RegularExpressions;

namespace media_management_app.Services;

/// <summary>
/// One rule for "this pack file is a movie", shared by the season grouper, the special bucket
/// detector and the inventory analyzer (PARSE-08).
/// </summary>
public static class PackMovieRules
{
    private static readonly Regex MovieWordRegex = new(
        @"(?:^|[^a-z0-9])(?:movies?|films?)(?:$|[^a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MoviesFolderRegex = new(
        @"^(?:movies?|films?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsUnderMoviesFolder(string relativePath) =>
        relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .SkipLast(1)
            .Any(MoviesFolderRegex.IsMatch);

    /// <summary>
    /// A file in a Movies/Films folder, or one whose name has the word "movie"/"film" and no
    /// SxxEyy. "S03E04.Movie.Night" and "Moviepass.S01E02" stay episodes.
    /// </summary>
    public static bool IsMovieFile(string relativePath, string fileName)
    {
        if (IsUnderMoviesFolder(relativePath))
        {
            return true;
        }

        if (!MovieWordRegex.IsMatch(Path.GetFileNameWithoutExtension(fileName)))
        {
            return false;
        }

        var parsed = TorrentCandidateParser.Parse(fileName, relativePath);
        return parsed.SeasonNumber is null || parsed.EpisodeNumber is null;
    }
}

namespace media_management_app.Models;

/// <summary>What <c>MoveEpisodeLinks</c> did with the links of renumbered episodes (audit L3-09).</summary>
public sealed class EpisodeLinkMoveResult
{
    /// <summary>
    /// Old episode numbers whose links cannot follow their episode (a multi-episode file whose episodes no longer
    /// follow each other, a file already at the new path). When any is set, nothing was changed.
    /// </summary>
    public HashSet<(int SeasonNumber, int EpisodeNumber)> BlockedKeys { get; } = [];

    /// <summary>A library file could not be renamed (open in a player, drive gone); every file was put back.</summary>
    public bool Failed { get; set; }

    public int MovedCount { get; set; }

    public List<string> Messages { get; } = [];
}

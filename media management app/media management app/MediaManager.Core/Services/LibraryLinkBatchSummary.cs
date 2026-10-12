namespace media_management_app.Services;

public static class LibraryLinkBatchSummary
{
    public static string FormatSymlinkCreated(
        string showTitle,
        int regularEpisodeCount,
        int matchedSpecialCount,
        int orphanExtraCount)
    {
        var title = string.IsNullOrWhiteSpace(showTitle) ? "Unknown Show" : showTitle.Trim();
        var parts = new List<string>(3);
        if (regularEpisodeCount > 0)
        {
            parts.Add(FormatCount(regularEpisodeCount, "episode", "episodes"));
        }

        if (matchedSpecialCount > 0)
        {
            parts.Add(FormatCount(matchedSpecialCount, "matched special", "matched specials"));
        }

        if (orphanExtraCount > 0)
        {
            parts.Add(FormatCount(orphanExtraCount, "extra", "extras"));
        }

        var detail = parts.Count == 0 ? "items" : string.Join(", ", parts);
        return $"{title} — {detail} symlinked";
    }

    public static (int RegularEpisodes, int MatchedSpecials, int OrphanExtras) CountMembers(
        IEnumerable<(bool IsOrphanPackSpecial, int? MappedSeasonNumber, int? SeasonNumber)> members)
    {
        var regular = 0;
        var specials = 0;
        var orphans = 0;
        foreach (var member in members)
        {
            switch (PackLinkMemberClassifier.Classify(
                member.IsOrphanPackSpecial,
                member.MappedSeasonNumber,
                member.SeasonNumber))
            {
                case PackLinkMemberKind.RegularEpisode:
                    regular++;
                    break;
                case PackLinkMemberKind.MatchedSpecial:
                    specials++;
                    break;
                case PackLinkMemberKind.OrphanExtra:
                    orphans++;
                    break;
            }
        }

        return (regular, specials, orphans);
    }

    private static string FormatCount(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";
}

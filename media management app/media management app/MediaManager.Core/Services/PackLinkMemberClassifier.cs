using media_management_app.Common;

namespace media_management_app.Services;

public enum PackLinkMemberKind
{
    RegularEpisode = 0,
    MatchedSpecial = 1,
    OrphanExtra = 2,
    Other = 3
}

public static class PackLinkMemberClassifier
{
    public static PackLinkMemberKind Classify(
        bool isOrphanPackSpecial,
        int? mappedSeasonNumber,
        int? seasonNumber)
    {
        if (isOrphanPackSpecial)
        {
            return PackLinkMemberKind.OrphanExtra;
        }

        var season = mappedSeasonNumber ?? seasonNumber;
        if (season == AppConstants.SpecialsSeasonNumber)
        {
            return PackLinkMemberKind.MatchedSpecial;
        }

        return season is > 0
            ? PackLinkMemberKind.RegularEpisode
            : PackLinkMemberKind.Other;
    }
}

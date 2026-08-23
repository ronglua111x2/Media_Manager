namespace media_management_app.Models;

public sealed class InferredEpisodePattern
{
    public static InferredEpisodePattern Invalid { get; } = new() { IsValid = false };

    public int PrefixLength { get; init; }

    public int SuffixLength { get; init; }

    public bool IsValid { get; init; }

    public int? CompactSeasonNumber { get; init; }

    public static InferredEpisodePattern ForCompactSeason(int seasonNumber) =>
        new()
        {
            CompactSeasonNumber = seasonNumber,
            IsValid = true
        };

    public int? TryExtractFixedWidth(string stem)
    {
        if (!IsValid || CompactSeasonNumber is not null || string.IsNullOrEmpty(stem))
        {
            return null;
        }

        var middleLength = stem.Length - PrefixLength - SuffixLength;
        if (middleLength <= 0)
        {
            return null;
        }

        var middle = stem.Substring(PrefixLength, middleLength);
        if (middle.Length == 0 || !middle.All(char.IsDigit))
        {
            return null;
        }

        return int.TryParse(middle, out var episode) ? episode : null;
    }
}

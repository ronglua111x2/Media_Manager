namespace media_management_app.Models;

public sealed class InferredSpecialPattern
{
    public static InferredSpecialPattern Invalid { get; } = new() { IsValid = false };

    public InferredSpecialNamingStyle Style { get; init; } = InferredSpecialNamingStyle.Unknown;

    public InferredEpisodePattern EpisodePattern { get; init; } = InferredEpisodePattern.Invalid;

    public bool IsValid { get; init; }
}

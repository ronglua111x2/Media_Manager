namespace media_management_app.Models;

public sealed class AutoTrackQualityPolicy
{
    public string? MinQuality { get; set; } = "1080p";

    public int MinSeeders { get; set; }

    public int? MinFileSizeMb { get; set; }

    public int? MaxFileSizeMb { get; set; }

    public List<string>? AllowedQualities { get; set; }
}

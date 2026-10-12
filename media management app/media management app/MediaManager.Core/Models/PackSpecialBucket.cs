namespace media_management_app.Models;

public sealed class PackSpecialBucket
{
    public SpecialLayoutKind LayoutKind { get; init; }

    public int? ParentSeasonNumber { get; init; }

    public List<(string RelativePath, string FileName)> Files { get; init; } = [];
}

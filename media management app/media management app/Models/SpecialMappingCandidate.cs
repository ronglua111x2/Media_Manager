namespace media_management_app.Models;

public sealed class SpecialMappingCandidate
{
    public string RelativePath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public int? ParentSeason { get; init; }

    public int LocalIndex { get; init; }

    public int GlobalSortOrder { get; init; }

    public SpecialLayoutKind LayoutKind { get; init; }

    public SpecialMappingNamingPattern NamingPattern { get; init; }
}

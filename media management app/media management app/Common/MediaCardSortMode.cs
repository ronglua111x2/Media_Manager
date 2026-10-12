namespace media_management_app.Common;

/// <summary>Legacy sort values from settings.json. Prefer <see cref="MediaCardSortField"/> + direction.</summary>
public enum MediaCardSortMode
{
    DateAddedDesc = 0,
    TypeThenTitle = 1,
    Title = 2
}

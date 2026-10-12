namespace media_management_app.Common;

public interface IMediaCardSortable
{
    string Title { get; }

    MediaKind MediaKind { get; }

    DateTime CreatedUtc { get; }

    double? Rating { get; }
}

public static class MediaCardSort
{
    public static bool DefaultIsAscending(MediaCardSortField field) => field switch
    {
        MediaCardSortField.DateAdded => false,
        MediaCardSortField.Rating => false,
        _ => true
    };

    public static (MediaCardSortField Field, bool IsAscending) FromLegacy(MediaCardSortMode mode) => mode switch
    {
        MediaCardSortMode.TypeThenTitle => (MediaCardSortField.Type, true),
        MediaCardSortMode.Title => (MediaCardSortField.Title, true),
        _ => (MediaCardSortField.DateAdded, false)
    };

    public static void Restore(
        MediaCardSortField? savedField,
        bool? savedAscending,
        MediaCardSortMode legacyMode,
        out MediaCardSortField field,
        out bool isAscending)
    {
        if (savedField is { } value)
        {
            field = value;
            isAscending = savedAscending ?? DefaultIsAscending(value);
            return;
        }

        (field, isAscending) = FromLegacy(legacyMode);
    }

    public static string GetDirectionToolTip(MediaCardSortField field, bool isAscending) => field switch
    {
        MediaCardSortField.DateAdded => isAscending ? "Oldest first" : "Newest first",
        MediaCardSortField.Type => isAscending ? "Shows first" : "Movies first",
        MediaCardSortField.Title => isAscending ? "A to Z" : "Z to A",
        MediaCardSortField.Rating => isAscending ? "Lowest rating first" : "Highest rating first",
        _ => isAscending ? "Ascending" : "Descending"
    };

    public static IEnumerable<T> Apply<T>(IEnumerable<T> source, MediaCardSortField field, bool isAscending)
        where T : IMediaCardSortable
    {
        IOrderedEnumerable<T> ordered = field switch
        {
            MediaCardSortField.Type => OrderBy(source, card => card.MediaKind, isAscending),
            MediaCardSortField.Title => OrderBy(source, card => card.Title, isAscending, StringComparer.OrdinalIgnoreCase),
            MediaCardSortField.Rating => OrderRating(source, isAscending),
            _ => OrderBy(source, card => card.CreatedUtc, isAscending)
        };

        return field is MediaCardSortField.Title or MediaCardSortField.Rating
            ? ordered
            : ordered.ThenBy(card => card.Title, StringComparer.OrdinalIgnoreCase);
    }

    private static IOrderedEnumerable<T> OrderRating<T>(IEnumerable<T> source, bool isAscending)
        where T : IMediaCardSortable
    {
        var unratedLast = source.OrderBy(card => card.Rating is > 0 ? 0 : 1);
        return isAscending
            ? unratedLast.ThenBy(card => card.Rating ?? 0).ThenBy(card => card.Title, StringComparer.OrdinalIgnoreCase)
            : unratedLast.ThenByDescending(card => card.Rating ?? 0).ThenBy(card => card.Title, StringComparer.OrdinalIgnoreCase);
    }

    private static IOrderedEnumerable<T> OrderBy<T, TKey>(IEnumerable<T> source, Func<T, TKey> keySelector, bool isAscending)
    {
        return isAscending ? source.OrderBy(keySelector) : source.OrderByDescending(keySelector);
    }

    private static IOrderedEnumerable<T> OrderBy<T>(
        IEnumerable<T> source,
        Func<T, string> keySelector,
        bool isAscending,
        StringComparer comparer)
    {
        return isAscending
            ? source.OrderBy(keySelector, comparer)
            : source.OrderByDescending(keySelector, comparer);
    }
}

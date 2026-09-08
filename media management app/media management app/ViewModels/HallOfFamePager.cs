using System.ComponentModel;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed class HallOfFamePager : INotifyPropertyChanged
{
    private IReadOnlyList<EpisodeHallOfFameEntry> _pool = [];
    private int _page;

    public IReadOnlyList<EpisodeHallOfFameEntry> Page { get; private set; } = CreateEmptyPage();

    public bool CanRotate => _pool.Count > PersonalRatingOverviewBuilder.HallOfFamePageSize;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetPool(IReadOnlyList<EpisodeHallOfFameEntry> pool)
    {
        _pool = pool;
        _page = 0;
        ShowCurrentPage();
    }

    public void Advance()
    {
        if (!CanRotate)
        {
            return;
        }

        _page = (_page + 1) % PageCount;
        ShowCurrentPage();
    }

    private int PageCount
    {
        get
        {
            var size = PersonalRatingOverviewBuilder.HallOfFamePageSize;
            return Math.Max(1, (_pool.Count + size - 1) / size);
        }
    }

    private void ShowCurrentPage()
    {
        var size = PersonalRatingOverviewBuilder.HallOfFamePageSize;
        var rows = new List<EpisodeHallOfFameEntry>(size);
        if (_pool.Count > 0)
        {
            var start = _page * size;
            var take = Math.Min(size, _pool.Count - start);
            for (var i = 0; i < take; i++)
            {
                rows.Add(_pool[start + i]);
            }
        }

        while (rows.Count < size)
        {
            rows.Add(EpisodeHallOfFameEntry.Placeholder);
        }

        Page = rows;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Page)));
    }

    private static IReadOnlyList<EpisodeHallOfFameEntry> CreateEmptyPage()
    {
        var size = PersonalRatingOverviewBuilder.HallOfFamePageSize;
        var rows = new EpisodeHallOfFameEntry[size];
        Array.Fill(rows, EpisodeHallOfFameEntry.Placeholder);
        return rows;
    }
}

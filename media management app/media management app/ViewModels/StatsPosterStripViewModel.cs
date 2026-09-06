using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class StatsPosterStripViewModel : ObservableObject
{
    private readonly List<TitleRatingCard> _source = [];
    private readonly List<StatsPosterCardViewModel> _pool = [];
    private readonly Func<StatsPosterCardViewModel, Task> _loadPoster;
    private int _fitCount;
    private int _pumpId;

    public StatsPosterStripViewModel(Func<StatsPosterCardViewModel, Task> loadPoster)
    {
        _loadPoster = loadPoster;
    }

    public ObservableCollection<StatsPosterCardViewModel> Visible { get; } = [];

    public int SourceCount => _source.Count;

    public bool CanExpand => IsExpanded || (_fitCount > 0 && _source.Count > _fitCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExpand))]
    private bool isExpanded;

    [ObservableProperty]
    private bool isLoading;

    public void ReplaceSource(IReadOnlyList<TitleRatingCard> source)
    {
        CancelPump();
        _source.Clear();
        _source.AddRange(source);
        _pool.Clear();
        Visible.Clear();
        IsLoading = false;
        OnPropertyChanged(nameof(CanExpand));
        OnPropertyChanged(nameof(SourceCount));
        if (IsExpanded && CanExpand)
        {
            BeginExpand();
            return;
        }

        IsExpanded = false;
        ShowCollapsed();
    }

    public void SetAvailableWidth(double width)
    {
        var fit = HeatmapStripLayout.PosterFitCount(width);
        if (fit == _fitCount)
        {
            OnPropertyChanged(nameof(CanExpand));
            return;
        }

        _fitCount = fit;
        OnPropertyChanged(nameof(CanExpand));
        if (IsExpanded)
        {
            return;
        }

        ShowCollapsed();
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        if (!IsExpanded && !CanExpand)
        {
            return;
        }

        IsExpanded = !IsExpanded;
        if (IsExpanded)
        {
            BeginExpand();
            return;
        }

        CancelPump();
        IsLoading = false;
        ShowCollapsed();
    }

    private void ShowCollapsed()
    {
        var count = _fitCount <= 0 ? 0 : Math.Min(_fitCount, _source.Count);
        EnsurePool(count);
        PublishVisible(count);
    }

    private void BeginExpand()
    {
        var remaining = _source.Count - _pool.Count;
        if (remaining <= HeatmapStripLayout.PosterVisualBatchSize)
        {
            EnsurePool(_source.Count);
            PublishVisible(_pool.Count);
            IsLoading = false;
            return;
        }

        IsLoading = true;
        QueuePump();
    }

    private void QueuePump()
    {
        var id = ++_pumpId;
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => Pump(id));
    }

    private void Pump(int id)
    {
        if (id != _pumpId || !IsExpanded)
        {
            return;
        }

        var take = HeatmapStripLayout.NextBatchCount(
            _source.Count - _pool.Count,
            HeatmapStripLayout.PosterVisualBatchSize);
        if (take <= 0)
        {
            IsLoading = false;
            PublishVisible(_pool.Count);
            return;
        }

        EnsurePool(_pool.Count + take);
        PublishVisible(_pool.Count);
        if (_pool.Count < _source.Count)
        {
            QueuePump();
            return;
        }

        IsLoading = false;
    }

    private void CancelPump() => _pumpId++;

    private void EnsurePool(int count)
    {
        count = Math.Min(count, _source.Count);
        while (_pool.Count < count)
        {
            var vm = new StatsPosterCardViewModel(_source[_pool.Count]);
            _pool.Add(vm);
            _ = _loadPoster(vm);
        }
    }

    private void PublishVisible(int count)
    {
        count = Math.Clamp(count, 0, _pool.Count);
        while (Visible.Count > count)
        {
            Visible.RemoveAt(Visible.Count - 1);
        }

        while (Visible.Count < count)
        {
            Visible.Add(_pool[Visible.Count]);
        }
    }
}

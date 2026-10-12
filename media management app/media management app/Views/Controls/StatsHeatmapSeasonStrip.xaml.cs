using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.ViewModels;

namespace media_management_app.Views.Controls;

public partial class StatsHeatmapSeasonStrip : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty CellsProperty =
        DependencyProperty.Register(
            nameof(Cells),
            typeof(IEnumerable),
            typeof(StatsHeatmapSeasonStrip),
            new PropertyMetadata(null, OnLayoutChanged));

    public static readonly DependencyProperty IsPreviewProperty =
        DependencyProperty.Register(
            nameof(IsPreview),
            typeof(bool),
            typeof(StatsHeatmapSeasonStrip),
            new PropertyMetadata(false, OnLayoutChanged));

    public static readonly DependencyProperty IsExtraProperty =
        DependencyProperty.Register(
            nameof(IsExtra),
            typeof(bool),
            typeof(StatsHeatmapSeasonStrip),
            new PropertyMetadata(false, OnLayoutChanged));

    public static readonly DependencyProperty IsTruncatedProperty =
        DependencyProperty.Register(
            nameof(IsTruncated),
            typeof(bool),
            typeof(StatsHeatmapSeasonStrip),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private readonly ObservableCollection<HeatmapEpisodeCell> _realized = [];
    private List<HeatmapEpisodeCell> _source = [];
    private int _nextIndex;
    private bool _pumpQueued;
    private bool _loadingReported;

    public StatsHeatmapSeasonStrip()
    {
        InitializeComponent();
        CellsHost.ItemsSource = _realized;
        SizeChanged += (_, _) =>
        {
            if (IsPreview)
            {
                RefreshVisible();
            }
        };
        Loaded += (_, _) => RefreshVisible();
        Unloaded += (_, _) => CancelPump(reportIdle: true);
    }

    public IEnumerable? Cells
    {
        get => (IEnumerable?)GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public bool IsPreview
    {
        get => (bool)GetValue(IsPreviewProperty);
        set => SetValue(IsPreviewProperty, value);
    }

    public bool IsExtra
    {
        get => (bool)GetValue(IsExtraProperty);
        set => SetValue(IsExtraProperty, value);
    }

    public bool IsTruncated
    {
        get => (bool)GetValue(IsTruncatedProperty);
        set => SetValue(IsTruncatedProperty, value);
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatsHeatmapSeasonStrip)d).RefreshVisible();

    private void RefreshVisible()
    {
        var cells = Cells?.OfType<HeatmapEpisodeCell>().ToList() ?? [];
        var slotWidth = IsExtra ? HeatmapStripLayout.ExtraSlotWidth : HeatmapStripLayout.StorySlotWidth;
        ClipToBounds = IsPreview;

        if (IsPreview)
        {
            CancelPump(reportIdle: true);
            var truncated = false;
            IReadOnlyList<HeatmapEpisodeCell> visible = cells;
            if (ActualWidth > 0)
            {
                var fitCount = HeatmapStripLayout.FitCount(ActualWidth, slotWidth);
                truncated = cells.Count > fitCount;
                visible = fitCount <= 0 ? [] : cells.Take(fitCount).ToList();
            }

            ReplaceRealized(visible);
            IsTruncated = truncated;
            return;
        }

        if (IsAlreadyRealized(cells))
        {
            IsTruncated = false;
            return;
        }

        CancelPump(reportIdle: true);
        _source = cells;
        _nextIndex = 0;
        _realized.Clear();
        IsTruncated = false;

        if (cells.Count <= HeatmapStripLayout.VisualBatchSize)
        {
            foreach (var cell in cells)
            {
                _realized.Add(cell);
            }

            return;
        }

        SetLoading(true);
        QueuePump();
    }

    private bool IsAlreadyRealized(IReadOnlyList<HeatmapEpisodeCell> cells)
    {
        if (_pumpQueued || _realized.Count != cells.Count)
        {
            return false;
        }

        for (var i = 0; i < cells.Count; i++)
        {
            if (!ReferenceEquals(_realized[i], cells[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void ReplaceRealized(IReadOnlyList<HeatmapEpisodeCell> visible)
    {
        if (IsAlreadyRealized(visible))
        {
            return;
        }

        _realized.Clear();
        foreach (var cell in visible)
        {
            _realized.Add(cell);
        }
    }

    private void QueuePump()
    {
        if (_pumpQueued)
        {
            return;
        }

        _pumpQueued = true;
        Dispatcher.BeginInvoke(Pump, DispatcherPriority.Background);
    }

    private void Pump()
    {
        _pumpQueued = false;
        if (!IsLoaded)
        {
            CancelPump(reportIdle: true);
            return;
        }

        var take = HeatmapStripLayout.NextBatchCount(_source.Count - _nextIndex);
        var end = _nextIndex + take;
        while (_nextIndex < end)
        {
            _realized.Add(_source[_nextIndex]);
            _nextIndex++;
        }

        if (_nextIndex < _source.Count)
        {
            QueuePump();
            return;
        }

        SetLoading(false);
    }

    private void CancelPump(bool reportIdle)
    {
        _pumpQueued = false;
        _source = [];
        _nextIndex = 0;
        if (reportIdle)
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool loading)
    {
        if (loading == _loadingReported)
        {
            return;
        }

        var row = FindRow();
        if (loading)
        {
            row?.BeginHeatmapLoad();
            _loadingReported = true;
            return;
        }

        if (_loadingReported)
        {
            row?.EndHeatmapLoad();
            _loadingReported = false;
        }
    }

    private StatsShowRowViewModel? FindRow()
    {
        DependencyObject current = this;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is StatsShowRowViewModel row)
            {
                return row;
            }

            current = VisualTreeHelper.GetParent(current)
                ?? LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}

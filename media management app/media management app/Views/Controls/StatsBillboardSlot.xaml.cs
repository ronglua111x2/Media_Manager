using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using media_management_app.ViewModels;

namespace media_management_app.Views.Controls;

public partial class StatsBillboardSlot : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(StatsBillboardSlot),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty IsPausedProperty =
        DependencyProperty.Register(
            nameof(IsPaused),
            typeof(bool),
            typeof(StatsBillboardSlot),
            new PropertyMetadata(false, OnPlaybackChanged));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(StatsBillboardSlot),
            new PropertyMetadata(false, OnPlaybackChanged));

    public static readonly DependencyProperty IntervalProperty =
        DependencyProperty.Register(
            nameof(Interval),
            typeof(TimeSpan),
            typeof(StatsBillboardSlot),
            new PropertyMetadata(TimeSpan.FromSeconds(8), OnPlaybackChanged));

    public static readonly DependencyProperty StartDelayProperty =
        DependencyProperty.Register(
            nameof(StartDelay),
            typeof(TimeSpan),
            typeof(StatsBillboardSlot),
            new PropertyMetadata(TimeSpan.Zero, OnPlaybackChanged));

    public static readonly DependencyProperty ExcludeIdentityProperty =
        DependencyProperty.Register(
            nameof(ExcludeIdentity),
            typeof(string),
            typeof(StatsBillboardSlot),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CurrentIdentityProperty =
        DependencyProperty.Register(
            nameof(CurrentIdentity),
            typeof(string),
            typeof(StatsBillboardSlot),
            new PropertyMetadata(null));

    private readonly DispatcherTimer _timer = new();
    private readonly List<StatsBillboardCardViewModel> _items = [];
    private INotifyCollectionChanged? _watchedCollection;
    private int _index;
    private bool _frontIsA = true;
    private bool _isAnimating;
    private bool _awaitingFirstTick = true;

    public StatsBillboardSlot()
    {
        InitializeComponent();
        _timer.Tick += OnTimerTick;
        Loaded += (_, _) => UpdatePlayback();
        Unloaded += (_, _) => StopTimer();
        SizeChanged += (_, _) => ResetTransforms();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool IsPaused
    {
        get => (bool)GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public TimeSpan Interval
    {
        get => (TimeSpan)GetValue(IntervalProperty);
        set => SetValue(IntervalProperty, value);
    }

    public TimeSpan StartDelay
    {
        get => (TimeSpan)GetValue(StartDelayProperty);
        set => SetValue(StartDelayProperty, value);
    }

    public string? ExcludeIdentity
    {
        get => (string?)GetValue(ExcludeIdentityProperty);
        set => SetValue(ExcludeIdentityProperty, value);
    }

    public string? CurrentIdentity
    {
        get => (string?)GetValue(CurrentIdentityProperty);
        set => SetValue(CurrentIdentityProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slot = (StatsBillboardSlot)d;
        slot.DetachCollection(e.OldValue as INotifyCollectionChanged);
        slot.AttachCollection(e.NewValue as INotifyCollectionChanged);
        slot.ReloadItems(reset: true);
    }

    private static void OnPlaybackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatsBillboardSlot)d).UpdatePlayback();

    private void AttachCollection(INotifyCollectionChanged? collection)
    {
        if (collection is null)
        {
            return;
        }

        _watchedCollection = collection;
        collection.CollectionChanged += OnItemsChanged;
    }

    private void DetachCollection(INotifyCollectionChanged? collection)
    {
        if (collection is null)
        {
            return;
        }

        collection.CollectionChanged -= OnItemsChanged;
        if (ReferenceEquals(_watchedCollection, collection))
        {
            _watchedCollection = null;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        ReloadItems(reset: true);

    private void ReloadItems(bool reset)
    {
        _items.Clear();
        if (ItemsSource is not null)
        {
            foreach (var item in ItemsSource.OfType<StatsBillboardCardViewModel>())
            {
                _items.Add(item);
            }
        }

        if (reset)
        {
            _index = 0;
            _isAnimating = false;
            _awaitingFirstTick = true;
            ShowCurrentWithoutAnimation();
        }

        UpdatePlayback();
    }

    private void ShowCurrentWithoutAnimation()
    {
        ResetTransforms();
        var current = CurrentItem();
        LayerA.Content = current;
        LayerA.Opacity = current is null ? 0 : 1;
        LayerA.IsHitTestVisible = current is not null;
        LayerB.Content = null;
        LayerB.Opacity = 0;
        LayerB.IsHitTestVisible = false;
        _frontIsA = true;
        CurrentIdentity = current?.IdentityKey;
    }

    private void UpdatePlayback()
    {
        if (!IsActive || IsPaused || _items.Count < 2 || !IsLoaded)
        {
            StopTimer();
            return;
        }

        _timer.Interval = _awaitingFirstTick && StartDelay > TimeSpan.Zero ? StartDelay : Interval;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void StopTimer()
    {
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_awaitingFirstTick)
        {
            _awaitingFirstTick = false;
            _timer.Interval = Interval;
        }

        Advance();
    }

    private void Advance()
    {
        if (_isAnimating || _items.Count < 2)
        {
            return;
        }

        var next = NextIndex(_index);
        if (next == _index)
        {
            return;
        }

        var incoming = _items[next];
        var front = _frontIsA ? LayerA : LayerB;
        var back = _frontIsA ? LayerB : LayerA;
        var frontTransform = _frontIsA ? LayerATransform : LayerBTransform;
        var backTransform = _frontIsA ? LayerBTransform : LayerATransform;
        var width = Viewport.ActualWidth;
        if (width <= 0)
        {
            width = ActualWidth;
        }

        if (width <= 0)
        {
            _index = next;
            ShowCurrentWithoutAnimation();
            return;
        }

        back.Content = incoming;
        back.Opacity = 1;
        back.IsHitTestVisible = false;
        backTransform.X = width;
        frontTransform.X = 0;

        _isAnimating = true;
        var duration = TimeSpan.FromMilliseconds(450);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var outgoing = new DoubleAnimation(0, -width, duration) { EasingFunction = ease };
        var incomingAnim = new DoubleAnimation(width, 0, duration) { EasingFunction = ease };
        incomingAnim.Completed += (_, _) =>
        {
            front.Opacity = 0;
            front.IsHitTestVisible = false;
            frontTransform.X = 0;
            back.IsHitTestVisible = true;
            backTransform.X = 0;
            _frontIsA = !_frontIsA;
            _index = next;
            CurrentIdentity = incoming.IdentityKey;
            _isAnimating = false;
        };

        frontTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, outgoing);
        backTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, incomingAnim);
    }

    private int NextIndex(int from)
    {
        if (_items.Count == 0)
        {
            return from;
        }

        var exclude = ExcludeIdentity;
        for (var step = 1; step <= _items.Count; step++)
        {
            var candidate = (from + step) % _items.Count;
            if (exclude is null || _items[candidate].IdentityKey != exclude || _items.Count == 1)
            {
                return candidate;
            }
        }

        return from;
    }

    private StatsBillboardCardViewModel? CurrentItem() =>
        _items.Count == 0 ? null : _items[Math.Clamp(_index, 0, _items.Count - 1)];

    private void ResetTransforms()
    {
        LayerATransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        LayerBTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        LayerATransform.X = 0;
        LayerBTransform.X = 0;
    }
}

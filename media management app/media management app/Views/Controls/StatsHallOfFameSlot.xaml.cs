using System.Collections;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using media_management_app.Models;

namespace media_management_app.Views.Controls;

public partial class StatsHallOfFameSlot : System.Windows.Controls.UserControl
{
    public const double RowHeight = 44;
    public const int PageSize = 10;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(StatsHallOfFameSlot),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(
            nameof(ItemTemplate),
            typeof(DataTemplate),
            typeof(StatsHallOfFameSlot),
            new PropertyMetadata(null, OnItemTemplateChanged));

    public static readonly DependencyProperty CanAnimateProperty =
        DependencyProperty.Register(
            nameof(CanAnimate),
            typeof(bool),
            typeof(StatsHallOfFameSlot),
            new PropertyMetadata(false));

    private bool _frontIsA = true;
    private bool _hasShown;
    private bool _isAnimating;
    private int _animToken;

    public StatsHallOfFameSlot()
    {
        InitializeComponent();
        Height = RowHeight * PageSize;
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public bool CanAnimate
    {
        get => (bool)GetValue(CanAnimateProperty);
        set => SetValue(CanAnimateProperty, value);
    }

    private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slot = (StatsHallOfFameSlot)d;
        var template = e.NewValue as DataTemplate;
        slot.LayerA.ItemTemplate = template;
        slot.LayerB.ItemTemplate = template;
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatsHallOfFameSlot)d).ApplyPage(animate: true);

    private void ApplyPage(bool animate)
    {
        var next = Snapshot(ItemsSource);
        if (!_hasShown || !CanAnimate || !animate || !IsLoaded || _isAnimating)
        {
            ShowImmediate(next);
            return;
        }

        AnimateTo(next);
    }

    private void ShowImmediate(IReadOnlyList<EpisodeHallOfFameEntry> page)
    {
        _animToken++;
        StopAnimations();
        LayerA.ItemsSource = page;
        LayerA.Opacity = 1;
        LayerA.IsHitTestVisible = true;
        LayerATransform.Y = 0;
        LayerB.ItemsSource = null;
        LayerB.Opacity = 0;
        LayerB.IsHitTestVisible = false;
        LayerBTransform.Y = 0;
        _frontIsA = true;
        _isAnimating = false;
        _hasShown = true;
    }

    private void AnimateTo(IReadOnlyList<EpisodeHallOfFameEntry> incoming)
    {
        var front = _frontIsA ? LayerA : LayerB;
        var back = _frontIsA ? LayerB : LayerA;
        var frontTransform = _frontIsA ? LayerATransform : LayerBTransform;
        var backTransform = _frontIsA ? LayerBTransform : LayerATransform;

        StopAnimations();
        back.ItemsSource = incoming;
        back.Opacity = 0;
        back.IsHitTestVisible = false;
        backTransform.Y = 14;
        front.Opacity = 1;
        frontTransform.Y = 0;

        _isAnimating = true;
        var token = ++_animToken;
        var duration = TimeSpan.FromMilliseconds(400);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeOut = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };
        var riseOut = new DoubleAnimation(0, -10, duration) { EasingFunction = ease };
        var riseIn = new DoubleAnimation(14, 0, duration) { EasingFunction = ease };
        fadeIn.Completed += (_, _) =>
        {
            if (token != _animToken)
            {
                return;
            }

            front.Opacity = 0;
            front.IsHitTestVisible = false;
            front.ItemsSource = null;
            frontTransform.Y = 0;
            back.Opacity = 1;
            back.IsHitTestVisible = true;
            backTransform.Y = 0;
            _frontIsA = !_frontIsA;
            _isAnimating = false;
        };

        front.BeginAnimation(OpacityProperty, fadeOut);
        back.BeginAnimation(OpacityProperty, fadeIn);
        frontTransform.BeginAnimation(TranslateTransform.YProperty, riseOut);
        backTransform.BeginAnimation(TranslateTransform.YProperty, riseIn);
    }

    private void StopAnimations()
    {
        LayerA.BeginAnimation(OpacityProperty, null);
        LayerB.BeginAnimation(OpacityProperty, null);
        LayerATransform.BeginAnimation(TranslateTransform.YProperty, null);
        LayerBTransform.BeginAnimation(TranslateTransform.YProperty, null);
        LayerATransform.Y = 0;
        LayerBTransform.Y = 0;
    }

    private static IReadOnlyList<EpisodeHallOfFameEntry> Snapshot(IEnumerable? source)
    {
        if (source is null)
        {
            return [];
        }

        return source.OfType<EpisodeHallOfFameEntry>().ToList();
    }
}

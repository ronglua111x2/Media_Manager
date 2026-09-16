using System.Windows;

namespace media_management_app.Views.Controls;

public partial class BusyOverlay : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(
            nameof(IsBusy),
            typeof(bool),
            typeof(BusyOverlay),
            new PropertyMetadata(false, OnIsBusyChanged));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(BusyOverlay),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(
            nameof(IsIndeterminate),
            typeof(bool),
            typeof(BusyOverlay),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(BusyOverlay),
            new PropertyMetadata(0d));

    public BusyOverlay()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnIsBusyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (BusyOverlay)d;
        var isBusy = (bool)e.NewValue;
        overlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        if (!isBusy || !overlay.IsIndeterminate)
        {
            return;
        }

        // Restart the indeterminate storyboard; it does not run while the overlay is Collapsed.
        overlay.ProgressBar.IsIndeterminate = false;
        overlay.ProgressBar.IsIndeterminate = true;
    }
}

using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfVisibility = System.Windows.Visibility;

namespace media_management_app.Views;

public partial class AppMessageBoxWindow : System.Windows.Window
{
    private WpfMessageBoxResult _dismissResult = WpfMessageBoxResult.OK;

    public AppMessageBoxWindow()
    {
        InitializeComponent();
        Result = WpfMessageBoxResult.OK;
    }

    public WpfMessageBoxResult Result { get; private set; }

    public void Configure(
        string message,
        string caption,
        WpfMessageBoxButton buttons,
        WpfMessageBoxImage image,
        WpfMessageBoxResult defaultResult)
    {
        Title = caption;
        TitleText.Text = caption;
        MessageText.Text = message;

        ApplySeverity(image);
        ApplyButtons(buttons, image);
        _dismissResult = ResolveDismissResult(buttons);
        Result = _dismissResult;

        var effectiveDefault = ResolveDefaultResult(buttons, defaultResult);
        ApplyDefaultButton(effectiveDefault);
    }

    private void ApplySeverity(WpfMessageBoxImage image)
    {
        if (image == WpfMessageBoxImage.None)
        {
            IconWell.Visibility = WpfVisibility.Collapsed;
            return;
        }

        IconWell.Visibility = WpfVisibility.Visible;

        if (IsError(image))
        {
            SeverityIcon.Kind = PackIconLucideKind.CircleAlert;
            SeverityIcon.Foreground = Brush("AppBrushDanger");
            IconWell.Background = Brush("AppBrushStatusErrorBg");
            return;
        }

        if (IsWarning(image))
        {
            SeverityIcon.Kind = PackIconLucideKind.TriangleAlert;
            SeverityIcon.Foreground = Brush("AppBrushWarning");
            IconWell.Background = Brush("AppBrushSurfaceRaised");
            return;
        }

        if (image == WpfMessageBoxImage.Question)
        {
            SeverityIcon.Kind = PackIconLucideKind.CircleQuestionMark;
            SeverityIcon.Foreground = Brush("AppBrushAccent");
            IconWell.Background = Brush("AppBrushAccentSoft");
            return;
        }

        SeverityIcon.Kind = PackIconLucideKind.Info;
        SeverityIcon.Foreground = Brush("AppBrushAccent");
        IconWell.Background = Brush("AppBrushAccentSoft");
    }

    private void ApplyButtons(WpfMessageBoxButton buttons, WpfMessageBoxImage image)
    {
        var primaryStyleKey = IsWarning(image) || IsError(image)
            ? "AppDangerButtonStyle"
            : "AppPrimaryButtonStyle";
        var primaryStyle = (System.Windows.Style)FindResource(primaryStyleKey);

        OkButton.Style = primaryStyle;
        YesButton.Style = primaryStyle;

        OkButton.Visibility = WpfVisibility.Collapsed;
        YesButton.Visibility = WpfVisibility.Collapsed;
        NoButton.Visibility = WpfVisibility.Collapsed;
        CancelButton.Visibility = WpfVisibility.Collapsed;
        OkButton.IsDefault = false;
        YesButton.IsDefault = false;
        NoButton.IsDefault = false;
        CancelButton.IsDefault = false;
        OkButton.IsCancel = false;
        YesButton.IsCancel = false;
        NoButton.IsCancel = false;
        CancelButton.IsCancel = false;

        switch (buttons)
        {
            case WpfMessageBoxButton.OKCancel:
                OkButton.Visibility = WpfVisibility.Visible;
                CancelButton.Visibility = WpfVisibility.Visible;
                CancelButton.IsCancel = true;
                break;
            case WpfMessageBoxButton.YesNo:
                YesButton.Visibility = WpfVisibility.Visible;
                NoButton.Visibility = WpfVisibility.Visible;
                NoButton.IsCancel = true;
                break;
            case WpfMessageBoxButton.YesNoCancel:
                YesButton.Visibility = WpfVisibility.Visible;
                NoButton.Visibility = WpfVisibility.Visible;
                CancelButton.Visibility = WpfVisibility.Visible;
                CancelButton.IsCancel = true;
                break;
            default:
                OkButton.Visibility = WpfVisibility.Visible;
                OkButton.IsCancel = true;
                break;
        }
    }

    private void ApplyDefaultButton(WpfMessageBoxResult defaultResult)
    {
        var defaultButton = defaultResult switch
        {
            WpfMessageBoxResult.Yes => YesButton,
            WpfMessageBoxResult.No => NoButton,
            WpfMessageBoxResult.Cancel => CancelButton,
            _ => OkButton.Visibility == WpfVisibility.Visible ? OkButton : YesButton
        };

        defaultButton.IsDefault = true;
        Loaded += (_, _) => defaultButton.Focus();
    }

    private static WpfMessageBoxResult ResolveDefaultResult(
        WpfMessageBoxButton buttons,
        WpfMessageBoxResult requested)
    {
        if (IsAvailable(buttons, requested))
        {
            return requested;
        }

        return buttons switch
        {
            WpfMessageBoxButton.OKCancel => WpfMessageBoxResult.OK,
            WpfMessageBoxButton.YesNo => WpfMessageBoxResult.Yes,
            WpfMessageBoxButton.YesNoCancel => WpfMessageBoxResult.Yes,
            _ => WpfMessageBoxResult.OK
        };
    }

    private static WpfMessageBoxResult ResolveDismissResult(WpfMessageBoxButton buttons) =>
        buttons switch
        {
            WpfMessageBoxButton.OKCancel => WpfMessageBoxResult.Cancel,
            WpfMessageBoxButton.YesNo => WpfMessageBoxResult.No,
            WpfMessageBoxButton.YesNoCancel => WpfMessageBoxResult.Cancel,
            _ => WpfMessageBoxResult.OK
        };

    private static bool IsAvailable(WpfMessageBoxButton buttons, WpfMessageBoxResult result) =>
        result switch
        {
            WpfMessageBoxResult.OK => buttons is WpfMessageBoxButton.OK or WpfMessageBoxButton.OKCancel,
            WpfMessageBoxResult.Cancel => buttons is WpfMessageBoxButton.OKCancel or WpfMessageBoxButton.YesNoCancel,
            WpfMessageBoxResult.Yes => buttons is WpfMessageBoxButton.YesNo or WpfMessageBoxButton.YesNoCancel,
            WpfMessageBoxResult.No => buttons is WpfMessageBoxButton.YesNo or WpfMessageBoxButton.YesNoCancel,
            _ => false
        };

    private static bool IsWarning(WpfMessageBoxImage image) =>
        image == WpfMessageBoxImage.Warning;

    private static bool IsError(WpfMessageBoxImage image) =>
        image == WpfMessageBoxImage.Error;

    private System.Windows.Media.Brush Brush(string key) =>
        (System.Windows.Media.Brush)FindResource(key);

    private void Complete(WpfMessageBoxResult result)
    {
        Result = result;
        Close();
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Complete(WpfMessageBoxResult.OK);

    private void YesButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Complete(WpfMessageBoxResult.Yes);

    private void NoButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Complete(WpfMessageBoxResult.No);

    private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Complete(WpfMessageBoxResult.Cancel);

    private void DismissButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Complete(_dismissResult);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject source &&
            IsInsideCloseButton(source))
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private bool IsInsideCloseButton(System.Windows.DependencyObject source)
    {
        System.Windows.DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, CloseCaptionButton))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}

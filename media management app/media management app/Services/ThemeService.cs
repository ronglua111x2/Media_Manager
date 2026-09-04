using System.Windows;
using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using media_management_app.Common;
using SkiaSharp;
using Wpf.Ui.Appearance;
using WpfApplicationTheme = Wpf.Ui.Appearance.ApplicationTheme;

namespace media_management_app.Services;

public sealed class ThemeService : IThemeService
{
    private const string LightColorsPath = "Resources/AppThemeColors.Light.xaml";
    private const string DarkColorsPath = "Resources/AppThemeColors.Dark.xaml";

    private static readonly System.Windows.Media.Color LightAccent = System.Windows.Media.Color.FromRgb(0x3d, 0x7d, 0x6b);
    private static readonly System.Windows.Media.Color DarkAccent = System.Windows.Media.Color.FromRgb(0x4f, 0xa8, 0x8f);

    private bool _hasApplied;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public void Apply(AppTheme theme)
    {
        if (_hasApplied && CurrentTheme == theme)
        {
            return;
        }

        SwapColorDictionary(theme);
        ApplyWpfUiTheme(theme);
        ApplyMaterialDesignTheme(theme);
        ApplyLiveChartsTheme(theme);
        CurrentTheme = theme;
        _hasApplied = true;
    }

    private static void SwapColorDictionary(AppTheme theme)
    {
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        var colorsPath = theme == AppTheme.Dark ? DarkColorsPath : LightColorsPath;

        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString;
            if (source is not null && source.Contains("AppThemeColors.", StringComparison.OrdinalIgnoreCase))
            {
                merged[i] = new ResourceDictionary
                {
                    Source = new Uri(colorsPath, UriKind.Relative)
                };
                return;
            }
        }
    }

    private static void ApplyWpfUiTheme(AppTheme theme)
    {
        var wpfTheme = theme == AppTheme.Dark ? WpfApplicationTheme.Dark : WpfApplicationTheme.Light;
        var accent = theme == AppTheme.Dark ? DarkAccent : LightAccent;
        ApplicationThemeManager.Apply(wpfTheme);
        ApplicationAccentColorManager.Apply(accent, wpfTheme);
    }

    private static void ApplyMaterialDesignTheme(AppTheme theme)
    {
        var paletteHelper = new PaletteHelper();
        var materialTheme = paletteHelper.GetTheme();
        materialTheme.SetBaseTheme(theme == AppTheme.Dark ? BaseTheme.Dark : BaseTheme.Light);
        materialTheme.SetPrimaryColor(theme == AppTheme.Dark ? DarkAccent : LightAccent);
        paletteHelper.SetTheme(materialTheme);
    }

    private static void ApplyLiveChartsTheme(AppTheme theme)
    {
        LiveCharts.Configure(config =>
        {
            if (theme == AppTheme.Dark)
            {
                config.AddDarkTheme(chartTheme => ApplyLiveChartsChrome(chartTheme, dark: true));
                return;
            }

            config.AddLightTheme(chartTheme => ApplyLiveChartsChrome(chartTheme, dark: false));
        });
    }

    private static void ApplyLiveChartsChrome(LiveChartsCore.Themes.Theme theme, bool dark)
    {
        theme.VirtualBackroundColor = new LvcColor(0, 0, 0, 0);
        theme.TooltipTextSize = 12;
        if (dark)
        {
            theme.TooltipBackgroundPaint = new SolidColorPaint(new SKColor(18, 24, 36, 235));
            theme.TooltipTextPaint = new SolidColorPaint(new SKColor(236, 240, 248));
            return;
        }

        theme.TooltipBackgroundPaint = new SolidColorPaint(new SKColor(255, 255, 255, 235));
        theme.TooltipTextPaint = new SolidColorPaint(new SKColor(28, 32, 40));
    }
}

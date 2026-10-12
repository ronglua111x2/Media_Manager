using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using media_management_app.Common;
using media_management_app.Services;

namespace media_management_app.Views;

public sealed class QueryTemplateHighlightText : TextBlock
{
    public static readonly DependencyProperty TemplateProperty = DependencyProperty.Register(
        nameof(Template),
        typeof(string),
        typeof(QueryTemplateHighlightText),
        new PropertyMetadata(null, OnHighlightChanged));

    public static readonly DependencyProperty TargetKindProperty = DependencyProperty.Register(
        nameof(TargetKind),
        typeof(MediaKind),
        typeof(QueryTemplateHighlightText),
        new PropertyMetadata(MediaKind.TvEpisode, OnHighlightChanged));

    public QueryTemplateHighlightText()
    {
        Loaded += (_, _) => RebuildInlines();
    }

    public string? Template
    {
        get => (string?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    public MediaKind TargetKind
    {
        get => (MediaKind)GetValue(TargetKindProperty);
        set => SetValue(TargetKindProperty, value);
    }

    private static void OnHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is QueryTemplateHighlightText control)
        {
            control.RebuildInlines();
        }
    }

    private void RebuildInlines()
    {
        Inlines.Clear();
        var template = Template;
        if (string.IsNullOrEmpty(template))
        {
            return;
        }

        var literalBrush = TryFindBrush(QueryTokenCatalog.LiteralBrushKey, Foreground);
        var invalidBrush = TryFindBrush(QueryTokenCatalog.InvalidBrushKey, literalBrush);
        var fallbackTokenBrush = TryFindBrush(QueryTokenCatalog.QualityBrushKey, literalBrush);

        foreach (var part in QueryTokenCatalog.SplitDisplayParts(template, TargetKind))
        {
            var run = new Run(part.Text);
            switch (part.Kind)
            {
                case QueryTemplatePartKind.ValidToken:
                    run.Foreground = TryFindBrush(
                        part.DisplayBrushKey ?? QueryTokenCatalog.QualityBrushKey,
                        fallbackTokenBrush);
                    run.FontWeight = FontWeights.SemiBold;
                    break;
                case QueryTemplatePartKind.InvalidToken:
                    run.Foreground = invalidBrush;
                    run.FontWeight = FontWeights.SemiBold;
                    break;
                default:
                    run.Foreground = literalBrush;
                    break;
            }

            Inlines.Add(run);
        }
    }

    private System.Windows.Media.Brush TryFindBrush(string key, System.Windows.Media.Brush fallback)
    {
        return TryFindResource(key) is System.Windows.Media.Brush brush ? brush : fallback;
    }
}

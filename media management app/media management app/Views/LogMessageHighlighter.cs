using System.Windows;
using System.Windows.Documents;
using media_management_app.Common;

namespace media_management_app.Views;

/// <summary>
/// Attached <c>Text</c> for a TextBlock that renders a log message as colored runs
/// (see <see cref="LogMessageTokenizer"/>). Brushes are resource references so they follow theme swaps.
/// Plain runs keep the TextBlock's own Foreground, so level-colored rows still tint ordinary text.
/// </summary>
public static class LogMessageHighlighter
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(LogMessageHighlighter),
            new PropertyMetadata(null, OnTextChanged));

    public static void SetText(DependencyObject element, string? value) =>
        element.SetValue(TextProperty, value);

    public static string? GetText(DependencyObject element) =>
        (string?)element.GetValue(TextProperty);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();
        foreach (var token in LogMessageTokenizer.Tokenize(e.NewValue as string))
        {
            var run = new Run(token.Text);
            switch (token.Kind)
            {
                case LogTokenKind.Quoted:
                    run.SetResourceReference(TextElement.ForegroundProperty, "AppBrushLogQuoted");
                    break;
                case LogTokenKind.Key:
                    run.SetResourceReference(TextElement.ForegroundProperty, "AppBrushLogKey");
                    break;
                case LogTokenKind.Value:
                    run.SetResourceReference(TextElement.ForegroundProperty, "AppBrushLogValue");
                    run.FontWeight = FontWeights.SemiBold;
                    break;
                case LogTokenKind.Number:
                    run.SetResourceReference(TextElement.ForegroundProperty, "AppBrushLogNumber");
                    break;
                case LogTokenKind.Url:
                    run.SetResourceReference(TextElement.ForegroundProperty, "AppBrushLogInfo");
                    run.TextDecorations = TextDecorations.Underline;
                    break;
            }

            textBlock.Inlines.Add(run);
        }
    }
}

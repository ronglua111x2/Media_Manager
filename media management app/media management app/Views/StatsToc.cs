using System.Windows;
using System.Windows.Media;

namespace media_management_app.Views;

public sealed class StatsTocEntry
{
    public required string Title { get; init; }

    public required FrameworkElement Target { get; init; }
}

public static class StatsToc
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.RegisterAttached(
            "Title",
            typeof(string),
            typeof(StatsToc),
            new PropertyMetadata(null));

    public static readonly DependencyProperty OrderProperty =
        DependencyProperty.RegisterAttached(
            "Order",
            typeof(int),
            typeof(StatsToc),
            new PropertyMetadata(0));

    public static void SetTitle(DependencyObject element, string? value) =>
        element.SetValue(TitleProperty, value);

    public static string? GetTitle(DependencyObject element) =>
        (string?)element.GetValue(TitleProperty);

    public static void SetOrder(DependencyObject element, int value) =>
        element.SetValue(OrderProperty, value);

    public static int GetOrder(DependencyObject element) =>
        (int)element.GetValue(OrderProperty);

    public static IReadOnlyList<StatsTocEntry> Collect(DependencyObject root)
    {
        var found = new List<(int Order, int Index, StatsTocEntry Entry)>();
        var index = 0;
        Walk(root, found, ref index);
        return found
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Index)
            .Select(item => item.Entry)
            .ToList();
    }

    private static void Walk(
        DependencyObject node,
        List<(int Order, int Index, StatsTocEntry Entry)> found,
        ref int index)
    {
        if (node is FrameworkElement element)
        {
            var title = GetTitle(element);
            if (!string.IsNullOrWhiteSpace(title) && IsShown(element))
            {
                found.Add((GetOrder(element), index, new StatsTocEntry
                {
                    Title = title.Trim(),
                    Target = element
                }));
                index++;
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            Walk(child, found, ref index);
        }
    }

    private static bool IsShown(FrameworkElement element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is UIElement ui && ui.Visibility == Visibility.Collapsed)
            {
                return false;
            }

            current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current);
        }

        return true;
    }
}

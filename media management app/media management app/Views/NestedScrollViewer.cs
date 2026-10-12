using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace media_management_app.Views;

/// <summary>
/// Nested ScrollViewers swallow the mouse wheel even when they only scroll horizontally.
/// Vertical wheel (no Shift) is forwarded to the ancestor page scroller; Shift+wheel pans horizontally.
/// </summary>
public static class NestedScrollViewer
{
    public static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scroller)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        var parent = FindAncestorScrollViewer(scroller);
        if (parent is null)
        {
            return;
        }

        parent.ScrollToVerticalOffset(parent.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject current)
    {
        var parent = VisualTreeHelper.GetParent(current);
        while (parent is not null)
        {
            if (parent is ScrollViewer viewer)
            {
                return viewer;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }
}

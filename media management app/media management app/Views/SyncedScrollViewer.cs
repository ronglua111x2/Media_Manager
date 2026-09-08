using System.Windows;
using System.Windows.Controls;

namespace media_management_app.Views;

public static class SyncedScrollViewer
{
    private static readonly Dictionary<string, List<ScrollViewer>> Groups = new(StringComparer.Ordinal);
    private static bool _isSyncing;

    public static readonly DependencyProperty GroupNameProperty =
        DependencyProperty.RegisterAttached(
            "GroupName",
            typeof(string),
            typeof(SyncedScrollViewer),
            new PropertyMetadata(null, OnGroupNameChanged));

    public static void SetGroupName(DependencyObject element, string? value) =>
        element.SetValue(GroupNameProperty, value);

    public static string? GetGroupName(DependencyObject element) =>
        (string?)element.GetValue(GroupNameProperty);

    private static void OnGroupNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        Detach(viewer);
        if (e.OldValue is string oldName)
        {
            Remove(oldName, viewer);
        }

        if (e.NewValue is string { Length: > 0 })
        {
            viewer.Loaded += OnLoaded;
            viewer.Unloaded += OnUnloaded;
            if (viewer.IsLoaded)
            {
                Attach(viewer);
            }
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer viewer)
        {
            Attach(viewer);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer viewer)
        {
            Detach(viewer);
            if (GetGroupName(viewer) is { Length: > 0 } name)
            {
                Remove(name, viewer);
            }
        }
    }

    private static void Attach(ScrollViewer viewer)
    {
        if (GetGroupName(viewer) is not { Length: > 0 } name)
        {
            return;
        }

        Add(name, viewer);
        viewer.ScrollChanged -= OnScrollChanged;
        viewer.ScrollChanged += OnScrollChanged;
    }

    private static void Detach(ScrollViewer viewer)
    {
        viewer.Loaded -= OnLoaded;
        viewer.Unloaded -= OnUnloaded;
        viewer.ScrollChanged -= OnScrollChanged;
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncing || e.VerticalChange == 0 || sender is not ScrollViewer source)
        {
            return;
        }

        if (GetGroupName(source) is not { Length: > 0 } name ||
            !Groups.TryGetValue(name, out var members))
        {
            return;
        }

        _isSyncing = true;
        try
        {
            foreach (var other in members)
            {
                if (!ReferenceEquals(other, source))
                {
                    other.ScrollToVerticalOffset(source.VerticalOffset);
                }
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private static void Add(string name, ScrollViewer viewer)
    {
        if (!Groups.TryGetValue(name, out var members))
        {
            members = [];
            Groups[name] = members;
        }

        if (!members.Contains(viewer))
        {
            members.Add(viewer);
        }
    }

    private static void Remove(string name, ScrollViewer viewer)
    {
        if (!Groups.TryGetValue(name, out var members))
        {
            return;
        }

        members.Remove(viewer);
        if (members.Count == 0)
        {
            Groups.Remove(name);
        }
    }
}

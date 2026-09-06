using System.Windows;
using System.Windows.Controls;

namespace media_management_app.Views.Controls;

public sealed class WorkspaceViewCache : ContentControl
{
    private readonly Dictionary<Type, FrameworkElement> _views = [];
    private bool _applying;

    public static readonly DependencyProperty WorkspaceProperty =
        DependencyProperty.Register(
            nameof(Workspace),
            typeof(object),
            typeof(WorkspaceViewCache),
            new PropertyMetadata(null, OnWorkspaceChanged));

    public object? Workspace
    {
        get => GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    private static void OnWorkspaceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WorkspaceViewCache)d).ApplyWorkspace(e.NewValue);

    private void ApplyWorkspace(object? workspace)
    {
        if (_applying)
        {
            return;
        }

        if (workspace is null)
        {
            Content = null;
            return;
        }

        var type = workspace.GetType();
        if (!_views.TryGetValue(type, out var view))
        {
            var key = new DataTemplateKey(type);
            var template = TryFindResource(key) as DataTemplate
                ?? System.Windows.Application.Current?.TryFindResource(key) as DataTemplate;
            if (template is null)
            {
                Content = workspace;
                return;
            }

            view = (FrameworkElement)template.LoadContent();
            _views[type] = view;
        }

        view.DataContext = workspace;
        _applying = true;
        try
        {
            Content = view;
        }
        finally
        {
            _applying = false;
        }
    }
}

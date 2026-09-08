using System.Windows;
using System.Windows.Input;

namespace media_management_app.Views;

public static class CommitTextBoxOnEnter
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(CommitTextBoxOnEnter),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBox box)
        {
            return;
        }

        box.PreviewKeyDown -= OnPreviewKeyDown;
        if (e.NewValue is true)
        {
            box.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not System.Windows.Controls.TextBox box)
        {
            return;
        }

        box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        Keyboard.ClearFocus();
        e.Handled = true;
    }
}

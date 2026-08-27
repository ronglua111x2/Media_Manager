using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using media_management_app.Models;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class QueryTemplateBuilderDialog : Window
{
    private readonly QueryTemplateBuilderDialogViewModel _viewModel;

    public QueryTemplateBuilderDialog(SearchRecipe recipe, IReadOnlyList<string> templates)
    {
        InitializeComponent();
        _viewModel = new QueryTemplateBuilderDialogViewModel(recipe, templates);
        DataContext = _viewModel;
        _viewModel.CaretMoved += (_, index) =>
        {
            PatternBox.Focus();
            PatternBox.CaretIndex = index;
        };
    }

    public IReadOnlyList<string> ResultTemplates => _viewModel.ResultTemplates;

    private void PatternBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box)
        {
            _viewModel.CaretIndex = box.SelectionStart;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CommitAndClose();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            CommitAndClose();
        }
    }

    private void CommitAndClose()
    {
        if (DialogResult != true)
        {
            DialogResult = true;
        }

        Close();
    }
}

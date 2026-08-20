using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class EnginePickerDialog : Window
{
    private readonly EnginePickerDialogViewModel _viewModel;

    public EnginePickerDialog(
        EnginePickerMode mode,
        IReadOnlyList<SearchPluginInfo> plugins,
        bool useAllEnabled,
        IReadOnlyList<string> selectedNames,
        EnginePriorityMode priorityMode,
        IReadOnlyList<IReadOnlyList<string>> rankGroups,
        bool isStaleList,
        IReadOnlyList<string>? lastCustomNames = null)
    {
        InitializeComponent();

        _viewModel = new EnginePickerDialogViewModel(
            mode,
            plugins,
            useAllEnabled,
            selectedNames,
            lastCustomNames,
            priorityMode,
            rankGroups,
            isStaleList);
        DataContext = _viewModel;
    }

    public bool ResultUseAllEnabled => _viewModel.ResultUseAllEnabled;

    public IReadOnlyList<string> ResultSelectedNames => _viewModel.ResultSelectedNames;

    public IReadOnlyList<string> ResultLastCustomNames => _viewModel.ResultLastCustomNames;

    public EnginePriorityMode ResultPriorityMode => _viewModel.ResultPriorityMode;

    public IReadOnlyList<IReadOnlyList<string>> ResultRankGroups => _viewModel.ResultRankGroups;

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MoveSelectedUp();
        KeepSelectedRankInView();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MoveSelectedDown();
        KeepSelectedRankInView();
    }

    private void MoveTop_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MoveSelectedToTop();
        KeepSelectedRankInView();
    }

    private void MoveBottom_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MoveSelectedToBottom();
        KeepSelectedRankInView();
    }

    private void KeepSelectedRankInView()
    {
        var selected = _viewModel.SelectedPriorityItem;
        if (selected is null)
        {
            return;
        }

        RankListBox.SelectedItem = selected;
        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(RankListBox.SelectedItem, selected))
            {
                RankListBox.SelectedItem = selected;
            }

            RankListBox.ScrollIntoView(selected);
            if (RankListBox.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem container)
            {
                container.IsSelected = true;
                container.BringIntoView();
            }
        }, DispatcherPriority.Background);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Validate(out var errorMessage))
        {
            System.Windows.MessageBox.Show(this, errorMessage, "Engines", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

using System.Windows;
using media_management_app.ViewModels;

namespace media_management_app.Views;

/// <summary>Find "Add + search": picks which cart orders to queue for a show.</summary>
public partial class QuickAddShowDialog : Window
{
    public QuickAddShowDialog(string mediaTitle, bool alreadyInLibrary)
    {
        InitializeComponent();
        TitleText.Text = mediaTitle;
        TitleText.ToolTip = mediaTitle;
        ConfirmButton.Content = alreadyInLibrary ? "Search now" : "Add + search";

        // "Library only" means nothing to do for an item that is already in the library.
        LibraryOnlyOption.Visibility = alreadyInLibrary ? Visibility.Collapsed : Visibility.Visible;
    }

    public QuickAddShowOption SelectedOption { get; private set; } = QuickAddShowOption.AllSeasonPacks;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        SelectedOption = LatestPackOption.IsChecked == true
            ? QuickAddShowOption.LatestSeasonPack
            : MissingEpisodesOption.IsChecked == true
                ? QuickAddShowOption.MissingEpisodes
                : LibraryOnlyOption.IsChecked == true
                    ? QuickAddShowOption.LibraryOnly
                    : QuickAddShowOption.AllSeasonPacks;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

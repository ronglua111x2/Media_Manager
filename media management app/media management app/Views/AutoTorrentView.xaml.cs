using System.Windows.Controls;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class AutoTorrentView : System.Windows.Controls.UserControl
{
    public AutoTorrentView()
    {
        InitializeComponent();
    }

    private void EpisodesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid { DataContext: TrackedSeasonViewModel season })
        {
            return;
        }

        season.SelectedEpisodes.Clear();
        foreach (var episode in ((DataGrid)sender).SelectedItems.OfType<TrackedEpisodeRowViewModel>())
        {
            season.SelectedEpisodes.Add(episode);
        }
    }
}

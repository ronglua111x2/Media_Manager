using System.Collections.ObjectModel;
using System.Windows;
using media_management_app.Models;

namespace media_management_app.Views;

public partial class SetAutoTrackDialog : Window
{
    public SetAutoTrackDialog(
        IReadOnlyList<TrackedEpisode> episodes,
        IReadOnlyList<string> downloadFolderOptions,
        string? defaultDownloadFolder,
        int? currentSeason,
        int? currentEpisode,
        string? currentDownloadFolder = null,
        bool autoReconcileAndLink = true)
    {
        InitializeComponent();

        var options = episodes
            .OrderBy(episode => episode.SeasonNumber)
            .ThenBy(episode => episode.EpisodeNumber)
            .Select(episode => new CheckpointOption(
                episode.SeasonNumber,
                episode.EpisodeNumber,
                $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} - {episode.Title}"))
            .ToList();

        CheckpointOptions = new ObservableCollection<CheckpointOption>(options);
        DownloadFolderOptions = new ObservableCollection<string>(downloadFolderOptions);
        DataContext = this;

        SelectedCheckpoint = options.FirstOrDefault(option =>
            currentSeason is not null &&
            currentEpisode is not null &&
            option.SeasonNumber == currentSeason &&
            option.EpisodeNumber == currentEpisode)
            ?? options.LastOrDefault();

        SelectedDownloadFolder = FirstNonEmpty(currentDownloadFolder, defaultDownloadFolder)
            ?? DownloadFolderOptions.FirstOrDefault();
        AutoReconcileAndLink = autoReconcileAndLink;
    }

    public ObservableCollection<CheckpointOption> CheckpointOptions { get; }

    public ObservableCollection<string> DownloadFolderOptions { get; }

    public CheckpointOption? SelectedCheckpoint { get; set; }

    public string? SelectedDownloadFolder { get; set; }

    public bool AutoReconcileAndLink { get; set; } = true;

    public int SelectedSeason => SelectedCheckpoint?.SeasonNumber ?? 1;

    public int SelectedEpisode => SelectedCheckpoint?.EpisodeNumber ?? 1;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCheckpoint is null)
        {
            System.Windows.MessageBox.Show(
                this,
                "Select a checkpoint episode.",
                "Auto-Track",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedDownloadFolder))
        {
            System.Windows.MessageBox.Show(
                this,
                "Select a download folder for auto-track.",
                "Auto-Track",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    public sealed record CheckpointOption(int SeasonNumber, int EpisodeNumber, string Label);
}

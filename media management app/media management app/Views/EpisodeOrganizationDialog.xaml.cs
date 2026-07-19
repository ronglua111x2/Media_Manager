using System.Collections.ObjectModel;
using System.Windows;
using media_management_app.Models;

namespace media_management_app.Views;

public partial class EpisodeOrganizationDialog : Window
{
    public EpisodeOrganizationDialog(
        string showTitle,
        int defaultSeasonCount,
        int defaultEpisodeCount,
        IReadOnlyList<TmdbEpisodeGroupSummary> episodeGroups,
        string? currentEpisodeGroupId = null,
        string confirmButtonText = "Add to Library")
    {
        InitializeComponent();

        ShowTitle = showTitle;
        ConfirmButtonText = confirmButtonText;

        var options = new List<OrganizationOption>
        {
            new(
                Id: null,
                Name: "Default (TMDB seasons)",
                SummaryLabel: FormatSummary(defaultSeasonCount, defaultEpisodeCount))
        };

        foreach (var group in episodeGroups)
        {
            options.Add(new OrganizationOption(group.Id, group.Name, group.SummaryLabel));
        }

        Options = new ObservableCollection<OrganizationOption>(options);
        DataContext = this;

        SelectedOption = options.FirstOrDefault(option =>
            string.Equals(option.Id, currentEpisodeGroupId, StringComparison.Ordinal))
            ?? options[0];

        var currentLabel = SelectedOption?.Name ?? "Default (TMDB seasons)";
        HelpText = string.Equals(confirmButtonText, "Apply", StringComparison.OrdinalIgnoreCase)
            ? $"Current organization: {currentLabel}.\nChoose a different organization to rebuild seasons/episodes. Hardlinks, candidates, and pack links will be cleared."
            : "Choose how seasons and episodes should be organized. Torrent releases often follow an Episode Group when it differs from TMDB's default seasons.";
    }

    public string ShowTitle { get; }

    public string HelpText { get; }

    public string ConfirmButtonText { get; }

    public ObservableCollection<OrganizationOption> Options { get; }

    public OrganizationOption? SelectedOption { get; set; }

    public string? SelectedEpisodeGroupId => SelectedOption?.Id;

    public string? SelectedEpisodeGroupName =>
        SelectedOption?.Id is null ? null : SelectedOption.Name;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedOption is null)
        {
            System.Windows.MessageBox.Show(
                this,
                "Select an episode organization.",
                "Episode Organization",
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

    private static string FormatSummary(int seasonCount, int episodeCount)
    {
        var seasons = Math.Max(0, seasonCount);
        var episodes = Math.Max(0, episodeCount);
        return $"{seasons} season{(seasons == 1 ? string.Empty : "s")} · {episodes} episode{(episodes == 1 ? string.Empty : "s")}";
    }

    public sealed record OrganizationOption(string? Id, string Name, string SummaryLabel);
}

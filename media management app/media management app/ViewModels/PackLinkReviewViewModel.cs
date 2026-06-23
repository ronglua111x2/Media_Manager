using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class PackLinkReviewViewModel : ObservableObject
{
    public PackLinkReviewViewModel()
    {
        Warnings = [];
    }

    public PackLinkReviewViewModel(SeasonPackLinkPreview preview)
        : this()
    {
        LoadPreview(preview);
    }

    [ObservableProperty]
    private string windowTitle = "Review AI mappings";

    [ObservableProperty]
    private string summaryText = string.Empty;

    public ObservableCollection<string> Warnings { get; }

    [ObservableProperty]
    private bool hasWarnings;

    public ObservableCollection<PackLinkReviewRowViewModel> Rows { get; } = [];

    public PackLinkReviewDecision Decision { get; private set; } = PackLinkReviewDecision.Cancel;

    public void LoadPreview(SeasonPackLinkPreview preview)
    {
        Decision = PackLinkReviewDecision.Cancel;
        WindowTitle = $"Review AI mappings — {preview.Show.DisplayTitle} S{preview.OwnerSeason.SeasonNumber:00}";

        var regularSummary = preview.RegularEpisodesBySeason.Count > 0
            ? PackLinkRegularEpisodeSummary.FormatSummary(preview.RegularEpisodesBySeason)
            : $"{preview.RegularEpisodeCount} regular episodes";

        SummaryText =
            $"Regular: {regularSummary}. Specials/OVAs: {preview.MatchedSpecialCount} matched, {preview.OrphanExtraCount} orphan extras.";

        Warnings.Clear();
        foreach (var warning in preview.SpecialMappings.Warnings)
        {
            Warnings.Add(warning);
        }

        HasWarnings = Warnings.Count > 0;
        Rows.Clear();
        BuildRows(preview);
    }

    [RelayCommand]
    private void Accept(Window? window)
    {
        Decision = PackLinkReviewDecision.Accept;
        if (window is not null)
        {
            window.DialogResult = true;
            window.Close();
        }
    }

    [RelayCommand]
    private void Retry(Window? window)
    {
        Decision = PackLinkReviewDecision.Retry;
        if (window is not null)
        {
            window.DialogResult = true;
            window.Close();
        }
    }

    [RelayCommand]
    private void Cancel(Window? window)
    {
        Decision = PackLinkReviewDecision.Cancel;
        if (window is not null)
        {
            window.DialogResult = false;
            window.Close();
        }
    }

    private void BuildRows(SeasonPackLinkPreview preview)
    {
        var episodesByKey = preview.SpecialMappings.Items
            .GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var file in preview.Inventory.Files
                     .Where(file => file.Classification is PackFileClassification.MatchedSpecial
                         or PackFileClassification.UnmatchedExtra)
                     .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            episodesByKey.TryGetValue(file.RelativePath, out var mappingItem);
            var isMatched = file.Classification == PackFileClassification.MatchedSpecial
                && file.MatchedEpisodeNumber is not null;

            Rows.Add(new PackLinkReviewRowViewModel
            {
                FileName = file.FileName,
                RelativePath = file.RelativePath,
                TargetLabel = isMatched
                    ? $"S{file.MatchedSeasonNumber:00}E{file.MatchedEpisodeNumber:00}"
                    : "Unmatched (orphan extras folder)",
                SourceLabel = FormatSource(file.MappingSource ?? mappingItem?.Source),
                Reason = string.IsNullOrWhiteSpace(file.MatchReason)
                    ? mappingItem?.Reason ?? string.Empty
                    : file.MatchReason,
                IsMatched = isMatched
            });
        }
    }

    private static string FormatSource(SpecialMappingSource? source) => source switch
    {
        SpecialMappingSource.Gemini => "Gemini AI",
        SpecialMappingSource.Rule => "Rules",
        _ => "—"
    };
}

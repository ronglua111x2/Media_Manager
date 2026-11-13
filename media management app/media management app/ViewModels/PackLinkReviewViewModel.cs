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

    /// <summary>Retry asks Gemini again, so it is only offered when this pack link used Gemini.</summary>
    [ObservableProperty]
    private bool canRetry;

    public ObservableCollection<PackLinkReviewRowViewModel> Rows { get; } = [];

    public PackLinkReviewDecision Decision { get; private set; } = PackLinkReviewDecision.Cancel;

    public void LoadPreview(SeasonPackLinkPreview preview)
    {
        Decision = PackLinkReviewDecision.Cancel;
        var kind = preview.SpecialMappings.UsedGemini ? "AI mappings" : "special/OVA mappings";
        WindowTitle = $"Review {kind} — {preview.Show.DisplayTitle} S{preview.OwnerSeason.SeasonNumber:00}";
        CanRetry = preview.UsedGeminiForSpecials;

        var regularSummary = preview.RegularEpisodesBySeason.Count > 0
            ? PackLinkRegularEpisodeSummary.FormatSummary(preview.RegularEpisodesBySeason)
            : $"{preview.RegularEpisodeCount} regular episodes";
        var skipped = preview.Inventory.Files.Count(file => file.Classification == PackFileClassification.Skipped);

        var model = preview.SpecialMappings.GeminiModel;
        SummaryText =
            $"Regular: {regularSummary}. Specials/OVAs: {preview.MatchedSpecialCount} matched, {preview.OrphanExtraCount} orphan extras." +
            (skipped > 0 ? $" {skipped} file(s) skipped." : string.Empty) +
            (string.IsNullOrWhiteSpace(model)
                ? string.Empty
                : preview.SpecialMappings.FromCache
                    ? $" Gemini answer from the cache ({model})."
                    : $" Answered by {model}.");

        Warnings.Clear();
        foreach (var warning in preview.SpecialMappings.Warnings.Concat(preview.Inventory.Warnings).Distinct(StringComparer.Ordinal))
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
        var itemsByPath = preview.SpecialMappings.Items
            .GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var specialRows = preview.Inventory.Files
            .Where(file => file.Classification is PackFileClassification.MatchedSpecial or PackFileClassification.UnmatchedExtra)
            .Select(file => BuildSpecialRow(file, itemsByPath.GetValueOrDefault(file.RelativePath)))
            .OrderBy(row => row.IsMatched ? 0 : 1)
            .ThenBy(row => row.RelativePath, StringComparer.OrdinalIgnoreCase);
        var skippedRows = preview.Inventory.Files
            .Where(file => file.Classification == PackFileClassification.Skipped)
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(file => new PackLinkReviewRowViewModel
            {
                FileName = file.FileName,
                RelativePath = file.RelativePath,
                TargetLabel = "Skipped (not linked)",
                SourceLabel = "—",
                Reason = file.MatchReason,
                IsSkipped = true,
                NeedsAttention = true
            });

        foreach (var row in specialRows.Concat(skippedRows))
        {
            Rows.Add(row);
        }
    }

    private static PackLinkReviewRowViewModel BuildSpecialRow(PackFileEntry file, SpecialMappingItem? item)
    {
        var isMatched = file.Classification == PackFileClassification.MatchedSpecial && file.MatchedEpisodeNumber is not null;
        var episode = isMatched && item?.Episode?.EpisodeNumber == file.MatchedEpisodeNumber ? item!.Episode : null;
        var proposalReason = file.ProposalReason ?? item?.ProposalReason;
        var source = file.MappingSource ?? item?.Source;

        return new PackLinkReviewRowViewModel
        {
            FileName = file.FileName,
            RelativePath = file.RelativePath,
            TargetLabel = isMatched
                ? $"S{file.MatchedSeasonNumber:00}E{file.MatchedEpisodeNumber:00}"
                : "Not mapped (orphan extras folder)",
            TmdbTitle = episode?.Title ?? string.Empty,
            AirDateLabel = episode?.AirDateDisplay ?? string.Empty,
            SourceLabel = FormatSource(source, isMatched ? proposalReason : null),
            Reason = string.IsNullOrWhiteSpace(file.MatchReason) ? item?.Reason ?? string.Empty : file.MatchReason,
            IsMatched = isMatched,
            NeedsAttention = !isMatched ||
                             item is null ||
                             item.MatchesSuggestion == false ||
                             !SpecialMappingReviewPolicy.IsTrustedWithoutReview(item) && proposalReason != SpecialMappingProposalReason.Window
        };
    }

    private static string FormatSource(SpecialMappingSource? source, SpecialMappingProposalReason? reason) =>
        source switch
        {
            SpecialMappingSource.Gemini => "Gemini AI",
            SpecialMappingSource.Rule => reason switch
            {
                SpecialMappingProposalReason.Direct => "S00Exx in name",
                SpecialMappingProposalReason.Window => "Season window",
                SpecialMappingProposalReason.Inferred => "Inferred",
                SpecialMappingProposalReason.Title => "Title match",
                SpecialMappingProposalReason.OvaNumber => "OVA number",
                SpecialMappingProposalReason.OvaOrder => "OVA order",
                SpecialMappingProposalReason.WindowWithoutAsides => "Window, recaps skipped",
                SpecialMappingProposalReason.Flatten => "Rules (flatten)",
                SpecialMappingProposalReason.Ordinal => "Rules (ordinal)",
                _ => "Rules"
            },
            _ => "—"
        };
}

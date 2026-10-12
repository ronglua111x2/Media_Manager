// Ported from Sonarr (https://github.com/Sonarr/Sonarr)
// Copyright (C) Sonarr contributors — licensed under GPL-3.0
// Modifications: removed Quality/Language model dependencies.

namespace MediaManager.Sonarr.Parser.Model;

public class ParsedEpisodeInfo
{
    public string ReleaseTitle { get; set; } = string.Empty;
    public string SeriesTitle { get; set; } = string.Empty;
    public SeriesTitleInfo? SeriesTitleInfo { get; set; }
    public int SeasonNumber { get; set; }
    public int[] EpisodeNumbers { get; set; } = Array.Empty<int>();
    public int[] AbsoluteEpisodeNumbers { get; set; } = Array.Empty<int>();
    public decimal[] SpecialAbsoluteEpisodeNumbers { get; set; } = Array.Empty<decimal>();
    public string? AirDate { get; set; }
    public bool FullSeason { get; set; }
    public bool IsPartialSeason { get; set; }
    public bool IsMultiSeason { get; set; }
    public bool IsSeasonExtra { get; set; }
    public bool IsSplitEpisode { get; set; }
    public bool IsMiniSeries { get; set; }
    public bool Special { get; set; }
    public string? ReleaseGroup { get; set; }
    public string? ReleaseHash { get; set; }
    public int SeasonPart { get; set; }
    public string? ReleaseTokens { get; set; }
    public int? DailyPart { get; set; }

    public bool IsDaily => !string.IsNullOrWhiteSpace(AirDate);

    public bool IsAbsoluteNumbering => AbsoluteEpisodeNumbers.Length > 0;

    public override string ToString()
    {
        if (IsDaily && EpisodeNumbers.Length == 0)
        {
            return $"{SeriesTitle} - {AirDate}";
        }

        if (FullSeason)
        {
            return $"{SeriesTitle} - Season {SeasonNumber:00}";
        }

        if (EpisodeNumbers.Length > 0)
        {
            return $"{SeriesTitle} - S{SeasonNumber:00}E{string.Join("-", EpisodeNumbers.Select(episode => episode.ToString("00")))}";
        }

        if (AbsoluteEpisodeNumbers.Length > 0)
        {
            return $"{SeriesTitle} - {string.Join("-", AbsoluteEpisodeNumbers.Select(episode => episode.ToString("000")))}";
        }

        return $"{SeriesTitle} - [Unknown Episode]";
    }
}

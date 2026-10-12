// Ported from Sonarr (https://github.com/Sonarr/Sonarr)
// Copyright (C) Sonarr contributors — licensed under GPL-3.0

namespace MediaManager.Sonarr.Parser.Model;

public class SeriesTitleInfo
{
    public string Title { get; set; } = string.Empty;
    public string TitleWithoutYear { get; set; } = string.Empty;
    public int Year { get; set; }
    public string[] AllTitles { get; set; } = Array.Empty<string>();
}

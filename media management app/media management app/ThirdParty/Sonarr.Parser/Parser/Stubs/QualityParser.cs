// Ported from Sonarr (https://github.com/Sonarr/Sonarr)
// Copyright (C) Sonarr contributors — licensed under GPL-3.0
// Modifications: stub — quality parsing not required for episode extraction.

namespace MediaManager.Sonarr.Parser.Stubs;

internal enum ParserQuality
{
    Unknown = 0
}

internal sealed class QualityModel
{
    public ParserQuality Quality { get; set; } = ParserQuality.Unknown;
}

internal static class QualityParser
{
    public static QualityModel ParseQuality(string title) => new();

    public static QualityModel ParseQualityName(string name) => new();
}

// Ported from Sonarr (https://github.com/Sonarr/Sonarr)
// Copyright (C) Sonarr contributors — licensed under GPL-3.0

using System.Text.RegularExpressions;

namespace MediaManager.Sonarr.Parser.Extensions;

internal static class RegexExtensions
{
    public static int EndIndex(this Capture capture) => capture.Index + capture.Length;
}

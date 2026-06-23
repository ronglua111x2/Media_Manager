// Ported from Sonarr (https://github.com/Sonarr/Sonarr)
// Copyright (C) Sonarr contributors — licensed under GPL-3.0
// Modifications: simplified media extension list.

using System.Text.RegularExpressions;

namespace MediaManager.Sonarr.Parser.Extensions;

public static class FileExtensions
{
    private static readonly Regex FileExtensionRegex = new(@"\.[a-z0-9]{2,4}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".wmv", ".mov", ".ts", ".m2ts", ".flv", ".webm",
        ".srt", ".ass", ".ssa", ".sub", ".idx",
        ".nfo", ".txt",
        ".par2", ".nzb",
        ".rar", ".zip", ".7z"
    };

    public static string RemoveFileExtension(string title)
    {
        return FileExtensionRegex.Replace(title, match =>
        {
            var extension = match.Value.ToLowerInvariant();
            return KnownExtensions.Contains(extension) ? string.Empty : match.Value;
        });
    }
}

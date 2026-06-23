// Ported from Sonarr (https://github.com/Sonarr/Sonarr)
// Copyright (C) Sonarr contributors — licensed under GPL-3.0
// Modifications: minimal subset for parser port.

using System.Globalization;
using System.Text;

namespace MediaManager.Sonarr.Parser.Extensions;

internal static class StringExtensions
{
    public static bool IsNullOrWhiteSpace(this string? text) => string.IsNullOrWhiteSpace(text);

    public static bool IsNotNullOrWhiteSpace(this string? text) => !string.IsNullOrWhiteSpace(text);

    public static bool ContainsIgnoreCase(this string text, string contains) =>
        text.Contains(contains, StringComparison.OrdinalIgnoreCase);

    public static string RemoveDiacritics(this string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

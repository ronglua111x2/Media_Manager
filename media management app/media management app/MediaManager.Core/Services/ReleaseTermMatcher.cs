namespace media_management_app.Services;

/// <summary>
/// Matches recipe terms (include, exclude, prefer, preferred audio, blocked groups) against a
/// torrent name. Matching is case-sensitive and whole-token: a term edge that is a letter or digit
/// must sit next to the start/end of the name or a non-alphanumeric character.
/// "CAM" matches "Movie.CAM.x264" and "[CAM]" but not "El Camino" or "HDCAM".
/// "TS" does not match "DTS"; "DV" does not match "DVD".
/// </summary>
public static class ReleaseTermMatcher
{
    public static bool Matches(string? text, string? term)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        var needle = term.Trim();
        var checkStart = char.IsLetterOrDigit(needle[0]);
        var checkEnd = char.IsLetterOrDigit(needle[^1]);

        var index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            var end = index + needle.Length;
            var startOk = !checkStart || index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var endOk = !checkEnd || end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (startOk && endOk)
            {
                return true;
            }

            index = text.IndexOf(needle, index + 1, StringComparison.Ordinal);
        }

        return false;
    }
}

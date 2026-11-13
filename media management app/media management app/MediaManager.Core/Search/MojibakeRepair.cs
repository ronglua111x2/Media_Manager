using System.Text;

namespace media_management_app.Search;

/// <summary>
/// Repairs UTF-8 titles that a site stored or served decoded as Windows-1252 ("Ð¡Ð²ÑÐ·ÑŒ" for "Связь",
/// "å½—æ˜Ÿæ¥" for "彗星来"). LimeTorrents and TorrentDownloads return such names (re-audit of 07c, bench 13/11/2026):
/// the garbage looks like Latin words before the title, so the movie title check rejected the release.
/// A string is only replaced when it maps back to bytes that are valid UTF-8; real Latin-1 text ("Amélie") is not.
/// </summary>
public static class MojibakeRepair
{
    // Windows-1252 characters for bytes 0x80-0x9F (bytes with no 1252 character stay as U+0080-U+009F).
    private static readonly Dictionary<char, byte> Cp1252 = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87,
        ['ˆ'] = 0x88, ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91,
        ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98,
        ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F
    };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Fix(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        // Only strings with a UTF-8 lead byte read as 1252 ("Â".."ô") can be mojibake.
        if (!text.Any(c => c is >= 'Â' and <= 'ô'))
        {
            return text;
        }

        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c <= 'ÿ')
            {
                bytes[i] = (byte)c;
            }
            else if (Cp1252.TryGetValue(c, out var b))
            {
                bytes[i] = b;
            }
            else
            {
                // A character 1252 cannot produce: the text is real Unicode, not mojibake.
                return text;
            }
        }

        try
        {
            var decoded = StrictUtf8.GetString(bytes);
            return decoded.Length < text.Length ? decoded : text;
        }
        catch (DecoderFallbackException)
        {
            return text;
        }
    }
}

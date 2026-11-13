using System.Text;
using System.Text.RegularExpressions;

namespace media_management_app.Services;

/// <summary>
/// Positional title check for movie releases whose year does not confirm the film (one year off, or no year):
/// audit 07c NS-01/02. A loose "two title words" match took the neighbouring film of a franchise ("Kill Bill Vol 1"
/// for Vol. 2, "Dune Part Two" for "Dune", "The Hunger Games Catching Fire" for "The Hunger Games").
/// <para>
/// One title variant must appear completely and in order. Words around it are only allowed when they cannot make
/// another film out of it: release tags (language, edition, source), bracketed metadata, non-Latin text (the
/// Japanese/Chinese/Korean title), another title variant ("Kimi no Na wa. Your Name."), "Movie 01" franchise
/// numbering, a release group before the title, or a separate name after " - ", "/", "|" or "aka".
/// Anything else right after the title ("Part Two", "II", "Fever", "Catching Fire", "Extra Scenes") or before it
/// ("Call Me by Your Name", "The Real Oppenheimer") means another film.
/// </para>
/// </summary>
public static partial class MovieReleaseTitle
{
    private static readonly HashSet<string> OptionalTitleWords = new(StringComparer.Ordinal) { "the", "a", "an", "and" };

    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        ["volume"] = "vol",
        ["pt"] = "part",
        ["n"] = "and"
    };

    private static readonly HashSet<string> MovieWords = new(StringComparer.Ordinal) { "movie", "film", "gekijouban", "gekijoban", "eiga" };

    // Words a release adds around a title without making it another film.
    private static readonly HashSet<string> TagWords = new(StringComparer.Ordinal)
    {
        // articles and "movie"
        "the", "a", "an", "movie", "film", "gekijouban", "gekijoban", "eiga", "anime",
        // editions
        "extended", "extend", "remastered", "remaster", "restored", "unrated", "uncut", "uncensored", "censored", "directors",
        "director", "cut", "theatrical", "imax", "enhanced", "edition", "special", "collectors", "collection",
        "criterion", "proper", "repack", "rerip", "internal", "limited", "complete", "hybrid", "dc", "se", "version",
        "ver", "anniversary", "ultimate", "final", "open", "matte", "colorized", "redux", "3d", "sbs", "hsbs", "hou",
        "extras", "bonus", "festival", "fin",
        // languages and subtitles
        "english", "eng", "french", "fre", "truefrench", "vff", "vfq", "vf", "vf2", "vfi", "vo", "vost", "vostfr",
        "subfrench", "german", "ger", "deu", "italian", "ita", "spanish", "spa", "esp", "castellano", "latino", "lat",
        "russian", "rus", "ukr", "ukrainian", "polish", "pol", "czech", "cze", "cz", "hungarian", "hun", "korean",
        "kor", "japanese", "jap", "jpn", "chinese", "chi", "chs", "cht", "mandarin", "cantonese", "hindi", "hin",
        "tamil", "telugu", "nordic", "swedish", "swe", "danish", "dan", "norwegian", "nor", "finnish", "fin", "dutch",
        "portuguese", "por", "brazilian", "turkish", "tur", "arabic", "ara", "thai", "vietnamese", "vie", "multi",
        "multisub", "multisubs", "dual", "audio", "dub", "dubbed", "dublado", "sub", "subs", "subbed", "subtitles",
        "subtitled", "hardsub", "softsub", "raw", "raws",
        // quality words that are not markers
        "hc", "hd", "fhd", "sd", "hq"
    };

    // Studio / franchise words that may precede a title ("Studio Ghibli - Spirited Away", "Disney Frozen").
    private static readonly HashSet<string> PrefixWords = new(StringComparer.Ordinal)
    {
        "studio", "ghibli", "disney", "disneys", "pixar", "pixars", "dreamworks", "marvel", "marvels", "dc", "netflix"
    };

    /// <param name="BracketGroup">Which top-level bracket the token sits in (-1 outside brackets).</param>
    private sealed record Token(string Text, bool Bracketed, int Segment, bool Possessive, bool GroupLike, int BracketGroup);

    /// <summary>
    /// True when <paramref name="releaseName"/> names the film of one of <paramref name="titleVariants"/> (see the class
    /// summary). Only meant for releases whose year does not already confirm the film.
    /// </summary>
    /// <param name="ignorePrefix">
    /// For releases whose year already matches: only the title itself and what follows it are checked (a same-year
    /// sequel such as "The Matrix Revolutions" for "The Matrix Reloaded"); words before it are not, because group
    /// tags, site names and titles in other languages ("L'ultima missione Project Hail Mary") are common there and the
    /// year confirms the film. Except for titles of one or two words: those are what other films' names contain
    /// ("Taxi Whats Your Name", "Burn Your Name" for "Your Name.", "The Real Oppenheimer"), so their prefix is checked.
    /// </param>
    public static bool MatchesTitle(string? releaseName, IReadOnlyList<string> titleVariants, bool ignorePrefix = false)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return false;
        }

        var variants = titleVariants
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(TitleNormalizer.MatchTokens)
            .Where(tokens => tokens.Any(token => !OptionalTitleWords.Contains(token)))
            .ToList();
        if (variants.Count == 0)
        {
            return false;
        }

        var titleWords = variants.SelectMany(tokens => tokens).ToHashSet(StringComparer.Ordinal);
        var tokens = Tokenize(releaseName);
        var end = RegionEnd(tokens, titleWords);
        return variants.Any(variant => MatchesVariant(tokens, end, variant, titleWords, ignorePrefix));
    }

    /// <summary>
    /// Release name tokens, normalized like titles, with whether each sits inside brackets, which name segment it
    /// belongs to (segments are split by " - ", "/", "|" and "aka") and whether it was written as a possessive ("Kon's").
    /// </summary>
    private static List<Token> Tokenize(string name)
    {
        // "Kimi.no.na.wa.A.K.A.Your.Name": a dotted "A.K.A" separates names like "aka".
        name = DottedAka().Replace(name, " aka ");
        var tokens = new List<Token>();
        var current = new StringBuilder();
        var depth = 0;
        var segment = 0;
        var possessive = false;
        var bracketGroup = -1;
        var bracketCount = 0;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            var raw = current.ToString();
            var text = TitleNormalizer.NormalizeToken(raw);
            current.Clear();
            if (text.Length == 0)
            {
                return;
            }

            if (text == "aka" && depth == 0)
            {
                segment++;
                return;
            }

            tokens.Add(new Token(text, depth > 0, segment, possessive, depth == 0 && IsGroupLike(raw), depth > 0 ? bracketGroup : -1));
            possessive = false;
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '\'' or '’' or '`' or '´')
            {
                // "Charlotte's" → "charlottes" (same as titles); remember a possessive "'s".
                if (i + 1 < name.Length && char.ToLowerInvariant(name[i + 1]) == 's' &&
                    (i + 2 >= name.Length || !char.IsLetterOrDigit(name[i + 2])))
                {
                    possessive = true;
                }

                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                // "绝命毒师电影El Camino": a change of script is a word boundary.
                if (current.Length > 0 && IsNonLatinLetter(current[^1]) != IsNonLatinLetter(c))
                {
                    Flush();
                }

                current.Append(c);
                continue;
            }

            Flush();
            switch (c)
            {
                case '[' or '(' or '{' or '【' or '「' or '『' or '（' or '［':
                    if (depth == 0)
                    {
                        bracketGroup = bracketCount++;
                    }

                    depth++;
                    break;
                case ']' or ')' or '}' or '】' or '」' or '』' or '）' or '］':
                    depth = Math.Max(0, depth - 1);
                    break;
                case '/' or '|' or '｜' or '／':
                    segment++;
                    break;
                case '-' when i > 0 && i + 1 < name.Length && char.IsWhiteSpace(name[i - 1]) && char.IsWhiteSpace(name[i + 1]):
                    segment++;
                    break;
            }
        }

        Flush();
        return tokens;
    }

    /// <summary>
    /// The title region ends at the first year or quality/source/codec/audio marker after a title word was seen
    /// (markers and years before the title belong to group tags or year-first names, and words of the title itself
    /// — "Charlotte's Web" — never end it).
    /// </summary>
    private static int RegionEnd(List<Token> tokens, HashSet<string> titleWords)
    {
        var seenTitle = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var text = tokens[i].Text;
            if (titleWords.Contains(text))
            {
                seenTitle = true;
                continue;
            }

            if (!seenTitle)
            {
                continue;
            }

            var pair = i + 1 < tokens.Count ? text + tokens[i + 1].Text : null;
            if (IsYear(text) || ReleaseMarker().IsMatch(text) || (pair is not null && !titleWords.Contains(pair) && ReleaseMarker().IsMatch(pair)))
            {
                return i;
            }
        }

        return tokens.Count;
    }

    private static bool MatchesVariant(List<Token> tokens, int end, IReadOnlyList<string> variant, HashSet<string> titleWords, bool ignorePrefix)
    {
        for (var start = 0; start < end; start++)
        {
            if (!TryMatchFrom(tokens, end, variant, start, titleWords, out var last))
            {
                continue;
            }

            var checkPrefix = !ignorePrefix || variant.Count(word => !OptionalTitleWords.Contains(word)) <= 2;
            if ((!checkPrefix || PrefixIsAllowed(tokens, start, titleWords)) && SuffixIsAllowed(tokens, end, last, titleWords))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches every required word of <paramref name="variant"/> in order starting exactly at <paramref name="start"/>.
    /// Words may be joined or split differently ("Spider-Man"/"Spiderman", "C'era"/"C era"), "Vol"/"Volume" are equal,
    /// and the gap between two title words may only hold tags, bracketed text, non-Latin text, title words or a
    /// "Movie 01" franchise number.
    /// </summary>
    private static bool TryMatchFrom(List<Token> tokens, int end, IReadOnlyList<string> variant, int start, HashSet<string> titleWords, out int last)
    {
        last = -1;
        var k = start;
        var v = 0;
        var first = true;
        while (v < variant.Count)
        {
            var word = variant[v];
            if (k >= end)
            {
                // Only optional words ("the", "a") may be left over.
                if (OptionalTitleWords.Contains(word))
                {
                    v++;
                    continue;
                }

                return false;
            }

            if (Same(tokens[k].Text, word))
            {
                last = k;
                k++;
                v++;
                first = false;
                continue;
            }

            if (v + 1 < variant.Count && tokens[k].Text == word + variant[v + 1])
            {
                last = k;
                k++;
                v += 2;
                first = false;
                continue;
            }

            if (k + 1 < end && tokens[k].Text + tokens[k + 1].Text == word)
            {
                last = k + 1;
                k += 2;
                v++;
                first = false;
                continue;
            }

            if (OptionalTitleWords.Contains(word))
            {
                v++;
                continue;
            }

            // The match has to start exactly at "start"; later gaps may only hold harmless words.
            if (first || !IsHarmless(tokens, k, titleWords))
            {
                return false;
            }

            k++;
        }

        return last >= 0;
    }

    private static bool Same(string token, string word) =>
        token == word || (Synonyms.TryGetValue(token, out var a) ? a : token) == (Synonyms.TryGetValue(word, out var b) ? b : word);

    private static bool PrefixIsAllowed(List<Token> tokens, int start, HashSet<string> titleWords)
    {
        var segment = tokens[start].Segment;
        var prefixStart = start;
        while (prefixStart > 0 && tokens[prefixStart - 1].Segment == segment)
        {
            prefixStart--;
        }

        if (prefixStart == start)
        {
            return true;
        }

        // "Satoshi Kon's Paprika": a possessive right before the title owns it.
        if (tokens[start - 1].Possessive)
        {
            return true;
        }

        // An unbracketed release group at the very start, written as one CamelCase word ("AnimeKaizoku Kimi no Na wa"):
        // real words before a title ("Skutecny Oppenheimer", "The Real Oppenheimer", "Call Me by Your Name") are not
        // written that way (audit 07c §6.1, 2026-11-13).
        if (prefixStart == 0 && start == 1 && tokens[0].GroupLike)
        {
            return true;
        }

        for (var i = prefixStart; i < start; i++)
        {
            if (!IsHarmless(tokens, i, titleWords, tokens[start].BracketGroup) && !PrefixWords.Contains(tokens[i].Text) && !IsYear(tokens[i].Text))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SuffixIsAllowed(List<Token> tokens, int end, int last, HashSet<string> titleWords)
    {
        var segment = tokens[last].Segment;
        for (var i = last + 1; i < end && tokens[i].Segment == segment; i++)
        {
            // A release group glued after the title, right before the quality/year marker that ends the title
            // ("Chou.Kaguya.hime.GoLTFilm.1080p.mkv"); sequel words ("Two", "II", "Fever") are not CamelCase, and a
            // CamelCase word that starts like one ("PartTwo") is not a group.
            if (tokens[i].GroupLike && i == end - 1 && end < tokens.Count && !SequelStart().IsMatch(tokens[i].Text))
            {
                continue;
            }

            if (!IsHarmless(tokens, i, titleWords, tokens[last].BracketGroup))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A release group written as one CamelCase word ("AnimeKaizoku", "GoLTFilm"): a lower-case letter followed by an
    /// upper-case one inside the word. Ordinary title words ("Real", "Two", "II") never are.
    /// </summary>
    private static bool IsGroupLike(string raw)
    {
        if (raw.Length < 5)
        {
            return false;
        }

        for (var i = 1; i < raw.Length; i++)
        {
            if (raw[i - 1] is >= 'a' and <= 'z' && raw[i] is >= 'A' and <= 'Z')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A word that cannot turn the title into another film. Bracketed text is metadata, except inside the bracket that
    /// holds the title itself (<paramref name="titleBracketGroup"/>): "[Kimi no Na wa X Tenki no Ko]" is two films.
    /// </summary>
    private static bool IsHarmless(List<Token> tokens, int i, HashSet<string> titleWords, int titleBracketGroup = -1)
    {
        var token = tokens[i];
        var text = token.Text;
        var metadataBracket = token.Bracketed && (titleBracketGroup < 0 || token.BracketGroup != titleBracketGroup);
        if (metadataBracket || titleWords.Contains(text) || TagWords.Contains(text) || IsNonLatin(text))
        {
            return true;
        }

        if (VersionOrOrdinal().IsMatch(text) || LanguageCombo().IsMatch(text))
        {
            return true;
        }

        // The name of an edition: "Libretto ver.", "Extend ver.", "Snyder Cut".
        if (i + 1 < tokens.Count && tokens[i + 1].Text is "ver" or "version" or "edition" or "cut")
        {
            return true;
        }

        // "Movie 01", "Film 3": franchise numbering of anime films.
        return text.All(char.IsDigit) && i > 0 && MovieWords.Contains(tokens[i - 1].Text);
    }

    private static bool IsNonLatin(string text) => text.Any(IsNonLatinLetter);

    private static bool IsNonLatinLetter(char c) => char.IsLetter(c) && c > 'ɏ';

    private static bool IsYear(string text) =>
        text.Length == 4 && (text.StartsWith("19", StringComparison.Ordinal) || text.StartsWith("20", StringComparison.Ordinal)) && text.All(char.IsDigit);

    [GeneratedRegex(@"(?<![A-Za-z])A\.K\.A(?:\.|(?![A-Za-z]))", RegexOptions.IgnoreCase)]
    private static partial Regex DottedAka();

    [GeneratedRegex(@"^(part|pt|vol|chapter|chap|book|episode|season|the|two|three|four|ii|iii|iv|\d)")]
    private static partial Regex SequelStart();

    [GeneratedRegex(@"^(v\d{1,2}|\d{1,3}(st|nd|rd|th))$")]
    private static partial Regex VersionOrOrdinal();

    [GeneratedRegex(@"^(en|eng|jp|jap|jpn|ja|fr|fre|ger|de|it|ita|es|spa|ru|rus|ko|kor|zh|chi|chs|cht|pt|por|br|multi|dual)+$")]
    private static partial Regex LanguageCombo();

    [GeneratedRegex(@"^(\d{3,4}[pi]|\d{3,4}x\d{3,4}|4k|8k|uhd|hdr|hdr10|hdr10plus|dv|dovi|sdr|bluray|blu|bdrip|brrip|bdremux|bd|bdmv|remux|web|webrip|webdl|hdtv|hdrip|dvdrip|dvd|dvdscr|fhdrip|x264|x265|h264|h265|hevc|avc|av1|xvid|divx|10bit|8bit|aac\d*|ac3|eac3|dd\d*|ddp\d*|dts|dtshd|truehd|atmos|flac|opus|mp3|nf|amzn|dsnp|hmax|atvp|mkv|mp4|avi)$")]
    private static partial Regex ReleaseMarker();
}

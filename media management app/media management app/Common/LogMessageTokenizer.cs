using System.Text.RegularExpressions;

namespace media_management_app.Common;

public enum LogTokenKind
{
    Plain,
    Quoted,
    Key,
    Value,
    Number,
    Url
}

public readonly record struct LogToken(LogTokenKind Kind, string Text);

/// <summary>
/// Splits a log message into highlightable tokens for the console's Pretty mode:
/// quoted strings, key=value pairs, numbers/dates/durations and URLs.
/// </summary>
public static partial class LogMessageTokenizer
{
    [GeneratedRegex(
        """(?<q>'[^'\r\n]*'|"[^"\r\n]*")|(?<url>https?://[^\s'"]+)|(?<k>(?<![\w.])[A-Za-z_][\w.\-]*=)(?<v>'[^'\r\n]*'|"[^"\r\n]*"|[^\s,;)]*)|(?<n>(?<![\w.])\d+(?:[.:/\-]\d+)*(?:%|ms|s|m|h|d|x|KB|MB|GB)?(?!\w))""",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public static IReadOnlyList<LogToken> Tokenize(string? message)
    {
        var tokens = new List<LogToken>();
        if (string.IsNullOrEmpty(message))
        {
            return tokens;
        }

        var position = 0;
        foreach (Match match in TokenRegex().Matches(message))
        {
            if (match.Index > position)
            {
                tokens.Add(new LogToken(LogTokenKind.Plain, message[position..match.Index]));
            }

            if (match.Groups["q"].Success)
            {
                tokens.Add(new LogToken(LogTokenKind.Quoted, match.Value));
            }
            else if (match.Groups["url"].Success)
            {
                tokens.Add(new LogToken(LogTokenKind.Url, match.Value));
            }
            else if (match.Groups["k"].Success)
            {
                tokens.Add(new LogToken(LogTokenKind.Key, match.Groups["k"].Value));
                var value = match.Groups["v"].Value;
                if (value.Length > 0)
                {
                    tokens.Add(new LogToken(LogTokenKind.Value, value));
                }
            }
            else
            {
                tokens.Add(new LogToken(LogTokenKind.Number, match.Value));
            }

            position = match.Index + match.Length;
        }

        if (position < message.Length)
        {
            tokens.Add(new LogToken(LogTokenKind.Plain, message[position..]));
        }

        return tokens;
    }
}

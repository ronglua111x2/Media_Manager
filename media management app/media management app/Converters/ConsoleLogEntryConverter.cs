using System.Globalization;
using System.Windows.Data;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.Converters;

/// <summary>
/// Projects an <see cref="AppLogEntry"/> into the display strings of a Pretty-mode console row.
/// ConverterParameter: Time, Level, Source, SourceFull, ExceptionHeader.
/// </summary>
public sealed class ConsoleLogEntryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not AppLogEntry entry)
        {
            return string.Empty;
        }

        return (parameter as string) switch
        {
            "Time" => entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            "Level" => AppLogger.ToAbbreviation(entry.Level),
            "Source" => FormatSource(ShortSourceName(entry.SourceFileName), entry.MemberName),
            "SourceFull" => FormatSource(entry.SourceFileName, entry.MemberName),
            "ExceptionHeader" => FormatExceptionHeader(entry.ExceptionText),
            _ => string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string ShortSourceName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return name.Length > "Service".Length && name.EndsWith("Service", StringComparison.Ordinal)
            ? name[..^"Service".Length]
            : name;
    }

    private static string FormatSource(string source, string member)
        => string.IsNullOrWhiteSpace(member) ? source : $"{source} · {member}";

    private static string FormatExceptionHeader(string? exceptionText)
    {
        if (string.IsNullOrWhiteSpace(exceptionText))
        {
            return string.Empty;
        }

        var lines = exceptionText.Split('\n');
        var first = lines[0].Trim();
        if (first.Length > 160)
        {
            first = first[..160] + "…";
        }

        return $"{first} · {lines.Length} lines";
    }
}

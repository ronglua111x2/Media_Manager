namespace media_management_app.Common;

/// <summary>
/// Case-insensitive comparer that orders digit runs by value ("- 2" before "- 10").
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer OrdinalIgnoreCase { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var i = 0;
        var j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsDigit(y[j]))
                {
                    j++;
                }

                var digitsX = x[startX..i].TrimStart('0');
                var digitsY = y[startY..j].TrimStart('0');
                var byLength = digitsX.Length.CompareTo(digitsY.Length);
                if (byLength != 0)
                {
                    return byLength;
                }

                var byValue = string.CompareOrdinal(digitsX, digitsY);
                if (byValue != 0)
                {
                    return byValue;
                }

                continue;
            }

            var byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        var byRemaining = (x.Length - i).CompareTo(y.Length - j);
        return byRemaining != 0 ? byRemaining : StringComparer.OrdinalIgnoreCase.Compare(x, y);
    }
}

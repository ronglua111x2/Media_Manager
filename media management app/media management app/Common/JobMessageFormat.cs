namespace media_management_app.Common;

public static class JobMessageFormat
{
    public static string Truncate(string? name, int keep = 5)
    {
        if (string.IsNullOrEmpty(name) || name.Length <= keep)
        {
            return name ?? string.Empty;
        }

        return name[..keep] + "...";
    }
}

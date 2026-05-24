namespace media_management_app.ViewModels;

public sealed class StorageStatusViewModel
{
    public string DriveRoot { get; init; } = string.Empty;

    public string Folder { get; init; } = string.Empty;

    public long TotalBytes { get; init; }

    public long FreeBytes { get; init; }

    public string TotalDisplay => FormatSize(TotalBytes);

    public string FreeDisplay => FormatSize(FreeBytes);

    public string UsedDisplay => FormatSize(Math.Max(0, TotalBytes - FreeBytes));

    public string FreePercentDisplay => TotalBytes <= 0 ? string.Empty : $"{(double)FreeBytes / TotalBytes:P0}";

    public bool IsLowSpace => TotalBytes > 0 && (double)FreeBytes / TotalBytes < 0.10;

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }
}

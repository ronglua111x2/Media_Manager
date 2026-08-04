namespace media_management_app.Models;

public sealed record SearchJobResults(string Status, IReadOnlyList<TorrentSearchResult> Results, int Total);

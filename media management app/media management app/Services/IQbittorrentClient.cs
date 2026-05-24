using media_management_app.Models;

namespace media_management_app.Services;

public interface IQbittorrentClient
{
    Task<string> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(TorrentSearchRequest request, CancellationToken cancellationToken = default);

    Task<AddedTorrentResult> AddTorrentAsync(AddTorrentRequest request, CancellationToken cancellationToken = default);
}

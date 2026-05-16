using media_management_app.Models;
using System.Net.Http;
using System.Net.Http.Headers;

namespace media_management_app.Services;

public sealed class TmdbMetadataProvider : IMetadataProvider
{
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public TmdbMetadataProvider(ISettingsService settingsService, HttpClient httpClient)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    }

    public Task<LibraryItem?> EnrichAsync(SourceItem item, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            return Task.FromResult<LibraryItem?>(null);
        }

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settingsService.Current.TmdbReadAccessToken);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Full metadata matching is intentionally deferred until the local scan/link loop works.
        return Task.FromResult<LibraryItem?>(null);
    }
}

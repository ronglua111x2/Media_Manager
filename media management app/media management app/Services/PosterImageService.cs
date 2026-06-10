using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using media_management_app.Common;

namespace media_management_app.Services;

public interface IPosterImageService
{
    Task<ImageSource?> LoadAsync(string? posterPath, CancellationToken cancellationToken = default);
}

public sealed class PosterImageService : IPosterImageService
{
    private static readonly HttpClient PosterHttpClient = new();
    private readonly Dictionary<string, ImageSource> _cache = [];
    private readonly IAppLogger _logger;

    public PosterImageService(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<ImageSource?> LoadAsync(string? posterPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(posterPath))
        {
            return null;
        }

        var posterUrl = $"https://image.tmdb.org/t/p/w342{posterPath}";
        if (_cache.TryGetValue(posterUrl, out var cachedImage))
        {
            return cachedImage;
        }

        try
        {
            var bytes = await PosterHttpClient.GetByteArrayAsync(posterUrl, cancellationToken);
            await using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            _cache[posterUrl] = image;
            return image;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Poster load failed for '{posterUrl}': {ex.Message}", LogTarget.File | LogTarget.Console);
            return null;
        }
    }
}

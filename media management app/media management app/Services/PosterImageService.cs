using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using media_management_app.Common;

namespace media_management_app.Services;

public interface IPosterImageService
{
    Task<ImageSource?> LoadAsync(
        string? posterPath,
        MediaKind? mediaKind = null,
        int? tmdbId = null,
        int width = 342,
        CancellationToken cancellationToken = default);

    string? GetDisplayUri(MediaKind mediaKind, int tmdbId, string? posterPath, int width = 342);

    Task EnsureCachedAsync(
        MediaKind mediaKind,
        int tmdbId,
        string? posterPath,
        CancellationToken cancellationToken = default);

    void DeleteCached(MediaKind mediaKind, int tmdbId);

    void DeleteAllCached();

    string? GetNotificationHeroImage(MediaKind mediaKind, int tmdbId, string? posterPath);
}

public sealed class PosterImageService : IPosterImageService
{
    private const int DefaultPosterWidth = 342;

    private static readonly HttpClient PosterHttpClient = new();
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly Dictionary<string, ImageSource> _memoryCache = [];

    public PosterImageService(ISettingsService settingsService, IAppLogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<ImageSource?> LoadAsync(
        string? posterPath,
        MediaKind? mediaKind = null,
        int? tmdbId = null,
        int width = DefaultPosterWidth,
        CancellationToken cancellationToken = default)
    {
        if (mediaKind is not null && tmdbId is not null && !string.IsNullOrWhiteSpace(posterPath))
        {
            var localPath = GetLocalFilePath(mediaKind.Value, tmdbId.Value);
            if (localPath is not null && IsCacheValid(mediaKind.Value, tmdbId.Value, posterPath))
            {
                return LoadImageFromFile(localPath);
            }

            var cached = await DownloadAndCacheAsync(
                mediaKind.Value,
                tmdbId.Value,
                posterPath,
                width,
                cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        if (string.IsNullOrWhiteSpace(posterPath))
        {
            return null;
        }

        return await LoadFromRemoteAsync(posterPath, width, cancellationToken);
    }

    public string? GetDisplayUri(MediaKind mediaKind, int tmdbId, string? posterPath, int width = DefaultPosterWidth)
    {
        if (!string.IsNullOrWhiteSpace(posterPath))
        {
            var localPath = GetLocalFilePath(mediaKind, tmdbId);
            if (localPath is not null && IsCacheValid(mediaKind, tmdbId, posterPath))
            {
                return new Uri(localPath).AbsoluteUri;
            }
        }

        return string.IsNullOrWhiteSpace(posterPath)
            ? null
            : BuildRemoteUrl(posterPath, width);
    }

    public async Task EnsureCachedAsync(
        MediaKind mediaKind,
        int tmdbId,
        string? posterPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(posterPath))
        {
            return;
        }

        var localPath = GetLocalFilePath(mediaKind, tmdbId);
        if (localPath is not null && IsCacheValid(mediaKind, tmdbId, posterPath))
        {
            return;
        }

        await DownloadAndCacheAsync(mediaKind, tmdbId, posterPath, DefaultPosterWidth, cancellationToken);
    }

    public void DeleteCached(MediaKind mediaKind, int tmdbId)
    {
        var imagePath = GetLocalFilePathForWrite(mediaKind, tmdbId);
        var metaPath = GetMetaFilePath(mediaKind, tmdbId);
        if (File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }

        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }

        RemoveMemoryCacheEntries(mediaKind, tmdbId);
    }

    public void DeleteAllCached()
    {
        var root = GetPostersRoot();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        _memoryCache.Clear();
    }

    public string? GetNotificationHeroImage(MediaKind mediaKind, int tmdbId, string? posterPath)
    {
        if (!string.IsNullOrWhiteSpace(posterPath))
        {
            var localPath = GetLocalFilePath(mediaKind, tmdbId);
            if (localPath is not null && IsCacheValid(mediaKind, tmdbId, posterPath))
            {
                return localPath;
            }
        }

        return string.IsNullOrWhiteSpace(posterPath)
            ? null
            : BuildRemoteUrl(posterPath, DefaultPosterWidth);
    }

    private string GetPostersRoot()
    {
        return Path.Combine(_settingsService.Current.StateFolder, AppConstants.PostersFolderName);
    }

    private static string GetMediaFolder(MediaKind mediaKind)
    {
        return mediaKind == MediaKind.Movie
            ? AppConstants.PosterMoviesFolderName
            : AppConstants.PosterShowsFolderName;
    }

    private string? GetLocalFilePath(MediaKind mediaKind, int tmdbId)
    {
        var path = GetLocalFilePathForWrite(mediaKind, tmdbId);
        return File.Exists(path) ? path : null;
    }

    private string GetLocalFilePathForWrite(MediaKind mediaKind, int tmdbId)
    {
        return Path.Combine(GetPostersRoot(), GetMediaFolder(mediaKind), $"{tmdbId}.jpg");
    }

    private string GetMetaFilePath(MediaKind mediaKind, int tmdbId)
    {
        return Path.Combine(GetPostersRoot(), GetMediaFolder(mediaKind), $"{tmdbId}.path");
    }

    private bool IsCacheValid(MediaKind mediaKind, int tmdbId, string posterPath)
    {
        var imagePath = GetLocalFilePathForWrite(mediaKind, tmdbId);
        if (!File.Exists(imagePath))
        {
            return false;
        }

        var metaPath = GetMetaFilePath(mediaKind, tmdbId);
        if (!File.Exists(metaPath))
        {
            return true;
        }

        return string.Equals(File.ReadAllText(metaPath), posterPath, StringComparison.Ordinal);
    }

    private async Task<ImageSource?> DownloadAndCacheAsync(
        MediaKind mediaKind,
        int tmdbId,
        string posterPath,
        int width,
        CancellationToken cancellationToken)
    {
        var remoteUrl = BuildRemoteUrl(posterPath, width);
        try
        {
            var bytes = await PosterHttpClient.GetByteArrayAsync(remoteUrl, cancellationToken);
            var folder = Path.Combine(GetPostersRoot(), GetMediaFolder(mediaKind));
            Directory.CreateDirectory(folder);

            var imagePath = GetLocalFilePathForWrite(mediaKind, tmdbId);
            await File.WriteAllBytesAsync(imagePath, bytes, cancellationToken);
            await File.WriteAllTextAsync(GetMetaFilePath(mediaKind, tmdbId), posterPath, cancellationToken);

            var image = LoadImageFromBytes(bytes);
            if (image is not null)
            {
                _memoryCache[BuildMemoryCacheKey(mediaKind, tmdbId)] = image;
            }

            return image;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                $"Poster cache failed for {mediaKind} tmdbId={tmdbId}: {ex.Message}",
                LogTarget.File | LogTarget.Console);
            return null;
        }
    }

    private async Task<ImageSource?> LoadFromRemoteAsync(
        string posterPath,
        int width,
        CancellationToken cancellationToken)
    {
        var remoteUrl = BuildRemoteUrl(posterPath, width);
        if (_memoryCache.TryGetValue(remoteUrl, out var cachedImage))
        {
            return cachedImage;
        }

        try
        {
            var bytes = await PosterHttpClient.GetByteArrayAsync(remoteUrl, cancellationToken);
            var image = LoadImageFromBytes(bytes);
            if (image is not null)
            {
                _memoryCache[remoteUrl] = image;
            }

            return image;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Poster load failed for '{remoteUrl}': {ex.Message}", LogTarget.File | LogTarget.Console);
            return null;
        }
    }

    private static ImageSource? LoadImageFromFile(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadImageFromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string BuildRemoteUrl(string posterPath, int width)
    {
        return $"https://image.tmdb.org/t/p/w{width}{posterPath}";
    }

    private static string BuildMemoryCacheKey(MediaKind mediaKind, int tmdbId)
    {
        return $"{mediaKind}:{tmdbId}";
    }

    private void RemoveMemoryCacheEntries(MediaKind mediaKind, int tmdbId)
    {
        _memoryCache.Remove(BuildMemoryCacheKey(mediaKind, tmdbId));
    }
}

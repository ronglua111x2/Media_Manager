using System.Globalization;
using System.Text;
using System.Xml;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class NfoWriterService : INfoWriterService
{
    private const string TmdbStillBaseUrl = "https://image.tmdb.org/t/p/w300";
    private const string TmdbPosterBaseUrl = "https://image.tmdb.org/t/p/w342";
    private const string TvShowNfoFileName = "tvshow.nfo";

    private readonly IAppLogger _logger;

    public NfoWriterService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void WriteEpisodeNfoIfNeeded(string symlinkPath, TrackedEpisode episode, TrackedShow show)
    {
        if (!show.UsesEpisodeGroup || string.IsNullOrWhiteSpace(symlinkPath))
        {
            return;
        }

        var nfoPath = Path.ChangeExtension(symlinkPath, ".nfo");
        if (string.IsNullOrWhiteSpace(nfoPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(nfoPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = CreateWriterSettings();
            using var stream = File.Create(nfoPath);
            using var writer = XmlWriter.Create(stream, settings);
            writer.WriteStartDocument(standalone: true);
            writer.WriteStartElement("episodedetails");
            WriteElement(writer, "title", episode.Title);
            WriteElement(writer, "showtitle", show.Title);
            WriteElement(writer, "season", episode.SeasonNumber.ToString(CultureInfo.InvariantCulture));
            WriteElement(writer, "episode", episode.EpisodeNumber.ToString(CultureInfo.InvariantCulture));
            WriteElement(writer, "aired", episode.AirDateDisplay);
            WriteElement(writer, "plot", episode.Overview);
            if (episode.VoteAverage is > 0)
            {
                WriteElement(writer, "rating", episode.VoteAverage.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(episode.StillPath))
            {
                WriteElement(writer, "thumb", BuildImageUrl(TmdbStillBaseUrl, episode.StillPath));
            }

            WriteUniqueId(writer, show.TmdbId);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to write episode NFO at {nfoPath}: {ex.Message}", LogTarget.All);
        }
    }

    public void WriteTvShowNfo(string showFolderPath, TrackedShow show)
    {
        if (!show.UsesEpisodeGroup || string.IsNullOrWhiteSpace(showFolderPath))
        {
            return;
        }

        var nfoPath = Path.Combine(showFolderPath, TvShowNfoFileName);
        try
        {
            Directory.CreateDirectory(showFolderPath);
            var settings = CreateWriterSettings();
            using var stream = File.Create(nfoPath);
            using var writer = XmlWriter.Create(stream, settings);
            writer.WriteStartDocument(standalone: true);
            writer.WriteStartElement("tvshow");
            WriteElement(writer, "title", show.Title);
            if (show.FirstAirYear is not null)
            {
                WriteElement(writer, "year", show.FirstAirYear.Value.ToString(CultureInfo.InvariantCulture));
            }

            WriteElement(writer, "plot", show.Overview);
            WriteElement(writer, "status", show.SeriesStatusLabel);
            WriteUniqueId(writer, show.TmdbId);
            if (!string.IsNullOrWhiteSpace(show.PosterPath))
            {
                writer.WriteStartElement("art");
                WriteElement(writer, "poster", BuildImageUrl(TmdbPosterBaseUrl, show.PosterPath));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to write tvshow.nfo at {nfoPath}: {ex.Message}", LogTarget.All);
        }
    }

    public void DeleteEpisodeNfo(string symlinkPath)
    {
        if (string.IsNullOrWhiteSpace(symlinkPath))
        {
            return;
        }

        var nfoPath = Path.ChangeExtension(symlinkPath, ".nfo");
        if (string.IsNullOrWhiteSpace(nfoPath) || !File.Exists(nfoPath))
        {
            return;
        }

        try
        {
            File.Delete(nfoPath);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to delete episode NFO at {nfoPath}: {ex.Message}", LogTarget.All);
        }
    }

    public void DeleteTvShowNfo(string showFolderPath)
    {
        if (string.IsNullOrWhiteSpace(showFolderPath))
        {
            return;
        }

        var nfoPath = Path.Combine(showFolderPath, TvShowNfoFileName);
        if (!File.Exists(nfoPath))
        {
            return;
        }

        try
        {
            File.Delete(nfoPath);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to delete tvshow.nfo at {nfoPath}: {ex.Message}", LogTarget.All);
        }
    }

    public bool HasRemainingEpisodeArtifacts(string showFolderPath)
    {
        if (string.IsNullOrWhiteSpace(showFolderPath) || !Directory.Exists(showFolderPath))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(showFolderPath, "*", SearchOption.AllDirectories)
                .Any(path => !string.Equals(Path.GetFileName(path), TvShowNfoFileName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to enumerate artifacts under {showFolderPath}: {ex.Message}", LogTarget.All);
            return true;
        }
    }

    public void CleanupOrphanEpisodeNfos(string showFolderPath, IEnumerable<string> activeSymlinkPaths)
    {
        if (string.IsNullOrWhiteSpace(showFolderPath) || !Directory.Exists(showFolderPath))
        {
            return;
        }

        var expectedNfos = activeSymlinkPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.ChangeExtension(path, ".nfo")!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> nfoFiles;
        try
        {
            nfoFiles = Directory.EnumerateFiles(showFolderPath, "*.nfo", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to enumerate NFO files under {showFolderPath}: {ex.Message}", LogTarget.All);
            return;
        }

        foreach (var nfoPath in nfoFiles)
        {
            if (string.Equals(Path.GetFileName(nfoPath), TvShowNfoFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullNfoPath = Path.GetFullPath(nfoPath);
            if (expectedNfos.Contains(fullNfoPath))
            {
                continue;
            }

            try
            {
                File.Delete(fullNfoPath);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to delete orphan NFO at {fullNfoPath}: {ex.Message}", LogTarget.All);
            }
        }
    }

    private static XmlWriterSettings CreateWriterSettings()
    {
        return new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false
        };
    }

    private static void WriteElement(XmlWriter writer, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        writer.WriteElementString(name, value.Trim());
    }

    private static void WriteUniqueId(XmlWriter writer, int tmdbId)
    {
        if (tmdbId <= 0)
        {
            return;
        }

        writer.WriteStartElement("uniqueid");
        writer.WriteAttributeString("type", "tmdb");
        writer.WriteAttributeString("default", "true");
        writer.WriteString(tmdbId.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    private static string BuildImageUrl(string baseUrl, string path)
    {
        var normalized = path.Trim();
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return baseUrl + normalized;
    }
}

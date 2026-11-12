using System.IO;
using System.Reflection;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Services.Events;
using media_management_app.Services.Symlink;
using Microsoft.Data.Sqlite;

namespace MediaManager.App.Tests.Link;

/// <summary>Interface fake built on <see cref="DispatchProxy"/>: unhandled members return defaults.</summary>
public class TestFake<T> : DispatchProxy where T : class
{
    private Func<MethodInfo, object?[]?, object?>? _handler;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var handled = _handler?.Invoke(targetMethod!, args);
        if (handled is not null)
        {
            return handled;
        }

        var returnType = targetMethod!.ReturnType;
        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    public static T Create(Func<MethodInfo, object?[]?, object?>? handler = null)
    {
        var proxy = Create<T, TestFake<T>>();
        ((TestFake<T>)(object)proxy)._handler = handler;
        return proxy;
    }
}

/// <summary>
/// Real link stack (SQLite database, hardlinks, symlink sync, reconciliation) on temporary folders.
/// Only qBittorrent, the symlink OS call, TMDB and the special-mapping orchestrator are faked.
/// Symlinks are written as plain files so tests need no admin rights.
/// </summary>
public sealed class LinkTestHost : IDisposable
{
    private LinkTestHost()
    {
    }

    public string Root { get; private set; } = string.Empty;

    public string Downloads { get; private set; } = string.Empty;

    public string Library { get; private set; } = string.Empty;

    public string Jellyfin { get; private set; } = string.Empty;

    public AppSettings Settings { get; } = new();

    public DatabaseService Db { get; private set; } = null!;

    public LibraryLinkEventHub Hub { get; private set; } = null!;

    public HardlinkService Hardlinks { get; private set; } = null!;

    public AutoTorrentLinkService Link { get; private set; } = null!;

    public SymlinkSyncService Symlinks { get; private set; } = null!;

    public TrackedShowService Shows { get; private set; } = null!;

    public TorrentReconciliationService Reconciliation { get; private set; } = null!;

    public List<AddedTorrentResult> Torrents { get; } = [];

    public Dictionary<string, List<TorrentContentFile>> TorrentFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fake symlink path → target path.</summary>
    public Dictionary<string, string> SymlinkTargets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Warnings { get; } = [];

    public static LinkTestHost Create()
    {
        var host = new LinkTestHost();
        host.Root = Path.Combine(Path.GetTempPath(), "mm-link-tests", Guid.NewGuid().ToString("N"));
        host.Downloads = Path.Combine(host.Root, "dl");
        host.Library = Path.Combine(host.Root, "lib");
        host.Jellyfin = Path.Combine(host.Root, "jf");
        Directory.CreateDirectory(host.Downloads);

        host.Settings.DriveLibraryRoots[Path.GetPathRoot(host.Root)!] = host.Library;
        host.Settings.Symlink.Enabled = true;
        host.Settings.Symlink.UnifiedRoot = host.Jellyfin;
        host.Settings.AutoTorrent.AutoLinkCompletedDownloads = false;

        var logger = TestFake<IAppLogger>.Create((method, args) =>
        {
            if (method.Name is "Warning" or "Error" or "Critical")
            {
                host.Warnings.Add($"{method.Name}: {args![0]}");
            }

            return null;
        });
        var settings = TestFake<ISettingsService>.Create((method, _) => method.Name == "get_Current" ? host.Settings : null);

        host.Db = new DatabaseService(logger);
        host.Db.Initialize(Path.Combine(host.Root, "state"));

        var qbittorrent = TestFake<IQbittorrentClient>.Create((method, args) => method.Name switch
        {
            "GetTorrentsAsync" => Task.FromResult<IReadOnlyList<AddedTorrentResult>>(host.Torrents.ToList()),
            "GetTorrentFilesAsync" => Task.FromResult<IReadOnlyList<TorrentContentFile>>(
                host.TorrentFiles.TryGetValue((string)args![0]!, out var files) ? files.ToList() : []),
            _ => null
        });

        host.Hub = new LibraryLinkEventHub();
        var resolver = new LibraryPathResolver(settings);
        host.Hardlinks = new HardlinkService(logger, resolver, settings, host.Hub);
        host.Link = new AutoTorrentLinkService(
            host.Db,
            qbittorrent,
            host.Hardlinks,
            host.Hub,
            TestFake<ISpecialMappingOrchestrator>.Create(),
            logger);

        var symlinkService = TestFake<ISymlinkService>.Create((method, args) =>
        {
            switch (method.Name)
            {
                case "IsRunningAsAdministrator":
                    return true;
                case "TryCreateFileSymlink":
                {
                    var path = Path.GetFullPath((string)args![0]!);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, "symlink -> " + args[1]);
                    host.SymlinkTargets[path] = Path.GetFullPath((string)args[1]!);
                    args[2] = null;
                    return true;
                }
                case "TryRemoveSymlink":
                {
                    var path = Path.GetFullPath((string)args![0]!);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    host.SymlinkTargets.Remove(path);
                    args[1] = null;
                    return true;
                }
                case "TryResolveSymlinkTarget":
                {
                    var path = Path.GetFullPath((string)args![0]!);
                    if (host.SymlinkTargets.TryGetValue(path, out var target))
                    {
                        args[1] = target;
                        return true;
                    }

                    args[1] = null;
                    return false;
                }
                case "IsSymlink":
                    return host.SymlinkTargets.ContainsKey(Path.GetFullPath((string)args![0]!));
            }

            return null;
        });

        host.Symlinks = new SymlinkSyncService(settings, host.Hardlinks, symlinkService, host.Db, logger);
        host.Shows = new TrackedShowService(
            host.Db,
            TestFake<ITmdbShowCatalogService>.Create(),
            TestFake<IPosterImageService>.Create(),
            logger);
        var movies = TestFake<ITrackedMovieService>.Create((method, _) => method.Name == "GetMovies" ? new List<TrackedMovie>() : null);
        host.Reconciliation = new TorrentReconciliationService(
            qbittorrent,
            host.Db,
            host.Shows,
            movies,
            host.Link,
            TestFake<IPackLinkCoordinatorService>.Create(),
            settings,
            logger);
        return host;
    }

    /// <summary>Adds a completed torrent whose files exist on disk under the downloads folder.</summary>
    public AddedTorrentResult AddTorrent(string hash, params string[] relativeFiles) =>
        AddNamedTorrent(hash, hash, relativeFiles);

    public AddedTorrentResult AddNamedTorrent(string hash, string name, params string[] relativeFiles)
    {
        var torrent = new AddedTorrentResult
        {
            Hash = hash,
            Name = name,
            Progress = 1,
            SavePath = Downloads,
            State = "uploading"
        };
        Torrents.Add(torrent);

        var files = new List<TorrentContentFile>();
        foreach (var relative in relativeFiles)
        {
            var fullPath = Path.Combine(Downloads, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "video " + relative);
            files.Add(new TorrentContentFile { Name = relative, Size = 100, Progress = 1 });
        }

        TorrentFiles[hash] = files;
        return torrent;
    }

    public long AddShow(int tmdbId, string title, params (int Season, int Episode, string Title)[] episodes)
    {
        var showId = Db.UpsertTrackedShow(new TrackedShow
        {
            TmdbId = tmdbId,
            Title = title,
            FirstAirYear = 2015,
            PreferredQuality = "1080p"
        });

        foreach (var season in episodes.Select(item => item.Season).Distinct())
        {
            Db.UpsertTrackedSeason(new TrackedSeason
            {
                ShowId = showId,
                SeasonNumber = season,
                EpisodeCount = episodes.Count(item => item.Season == season)
            });
        }

        foreach (var episode in episodes)
        {
            Db.UpsertTrackedEpisode(new TrackedEpisode
            {
                ShowId = showId,
                SeasonNumber = episode.Season,
                EpisodeNumber = episode.Episode,
                Title = episode.Title
            });
        }

        return showId;
    }

    public void AssignEpisodeTorrent(long showId, int season, int episode, string hash)
    {
        var tracked = Db.GetTrackedEpisodes(showId).Single(item => item.SeasonNumber == season && item.EpisodeNumber == episode);
        Db.UpdateTrackedEpisodeTorrent(tracked.Id, hash, hash, "uploading", 1);
    }

    /// <summary>Puts season <paramref name="ownerSeason"/> in pack mode, owned by torrent <paramref name="hash"/>.</summary>
    public void UsePack(long showId, int ownerSeason, string hash)
    {
        Db.UpdateTrackedSeasonPackMode(showId, ownerSeason, SeasonManagementMode.Pack);
        Db.UpdateTrackedSeasonSelectedPack(showId, ownerSeason, new SeasonPackCandidate
        {
            FileName = hash,
            CoveredSeasons = [ownerSeason]
        });
        Db.UpdateTrackedSeasonPackTorrent(showId, ownerSeason, Torrents.Single(item => item.Hash == hash));
    }

    /// <summary>Applies a pack link with a hand-built inventory, through the same apply path the app uses after review.</summary>
    public Task<AutoTorrentLinkResult> ApplyPackAsync(
        long showId,
        int ownerSeason,
        params (string RelativePath, PackFileClassification Classification, int? Season, int? Episode)[] files)
    {
        var show = Db.GetTrackedShow(showId)!;
        var season = Db.GetTrackedSeasons(showId).Single(item => item.SeasonNumber == ownerSeason);
        var preview = new SeasonPackLinkPreview
        {
            Show = show,
            OwnerSeason = season,
            Inventory = new PackTorrentInventory
            {
                Files = files.Select(file => new PackFileEntry
                {
                    RelativePath = file.RelativePath,
                    FileName = Path.GetFileName(file.RelativePath),
                    Classification = file.Classification,
                    MatchedSeasonNumber = file.Season,
                    MatchedEpisodeNumber = file.Episode
                }).ToList()
            },
            SpecialMappings = new SpecialMappingResult()
        };
        return Link.ApplySeasonPackLinkAsync(preview);
    }

    public EpisodeAvailability Availability(long showId, int season, int episode)
    {
        Shows.RefreshAvailability(showId);
        return Db.GetTrackedEpisodes(showId)
            .Single(item => item.SeasonNumber == season && item.EpisodeNumber == episode)
            .Availability;
    }

    public IReadOnlyList<SourceItem> Items() => Db.GetSourceItems();

    public IReadOnlyList<string> JellyfinFiles() =>
        Directory.Exists(Jellyfin)
            ? Directory.EnumerateFiles(Jellyfin, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(Jellyfin, path)).ToList()
            : [];

    public IReadOnlyList<string> LibraryFiles() =>
        Directory.Exists(Library)
            ? Directory.EnumerateFiles(Library, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(Library, path)).ToList()
            : [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort: a locked temp file must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

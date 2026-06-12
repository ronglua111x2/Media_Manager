using Microsoft.Data.Sqlite;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class DatabaseService : IDatabaseService
{
    private readonly IAppLogger _logger;
    private string _connectionString = string.Empty;

    public DatabaseService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void Initialize(string stateFolder)
    {
        Directory.CreateDirectory(stateFolder);
        var databasePath = Path.Combine(stateFolder, "media-manager.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        _logger.Info($"Initializing SQLite database at {databasePath}", LogTarget.File | LogTarget.Console);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SourceItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceRootFolder TEXT NOT NULL,
                ParentFolder TEXT NOT NULL,
                FilePath TEXT NOT NULL UNIQUE,
                FileName TEXT NOT NULL,
                ScanText TEXT NOT NULL,
                MediaKind INTEGER NOT NULL DEFAULT 0,
                ParserPattern INTEGER NOT NULL DEFAULT 0,
                ShowTitle TEXT NULL,
                MovieTitle TEXT NULL,
                MovieYear INTEGER NULL,
                SeasonNumber INTEGER NULL,
                EpisodeNumber INTEGER NULL,
                MappedSeasonNumber INTEGER NULL,
                MappedEpisodeNumber INTEGER NULL,
                EpisodeMappingSource TEXT NULL,
                EpisodeMappingConfidence REAL NULL,
                EpisodeMappingReason TEXT NULL,
                EpisodeTitle TEXT NULL,
                MatchedTitle TEXT NULL,
                MatchedYear INTEGER NULL,
                Provider TEXT NULL,
                ProviderId TEXT NULL,
                MatchConfidence REAL NULL,
                MatchReason TEXT NULL,
                RequiresManualReview INTEGER NOT NULL DEFAULT 0,
                MatchAccepted INTEGER NOT NULL DEFAULT 0,
                UseAbsoluteAnimeMapping INTEGER NOT NULL DEFAULT 0,
                State INTEGER NOT NULL,
                Notes TEXT NULL,
                LinkedPath TEXT NULL,
                AutoTorrentLinkKind INTEGER NULL,
                AutoTorrentTorrentHash TEXT NULL,
                AutoTorrentPackOwnerSeasonNumber INTEGER NULL,
                IsExternalImport INTEGER NOT NULL DEFAULT 0,
                LastSeenUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "SourceItems", "MediaKind", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SourceItems", "ParserPattern", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SourceItems", "MovieTitle", "TEXT NULL");
        EnsureColumn(connection, "SourceItems", "MovieYear", "INTEGER NULL");
        EnsureColumn(connection, "SourceItems", "MappedSeasonNumber", "INTEGER NULL");
        EnsureColumn(connection, "SourceItems", "MappedEpisodeNumber", "INTEGER NULL");
        EnsureColumn(connection, "SourceItems", "EpisodeMappingSource", "TEXT NULL");
        EnsureColumn(connection, "SourceItems", "EpisodeMappingConfidence", "REAL NULL");
        EnsureColumn(connection, "SourceItems", "EpisodeMappingReason", "TEXT NULL");
        EnsureColumn(connection, "SourceItems", "MatchedTitle", "TEXT NULL");
        EnsureColumn(connection, "SourceItems", "MatchedYear", "INTEGER NULL");
        EnsureColumn(connection, "SourceItems", "Provider", "TEXT NULL");
        EnsureColumn(connection, "SourceItems", "ProviderId", "TEXT NULL");
        EnsureColumn(connection, "SourceItems", "MatchConfidence", "REAL NULL");
        EnsureColumn(connection, "SourceItems", "MatchReason", "TEXT NULL");
        EnsureColumn(connection, "SourceItems", "RequiresManualReview", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SourceItems", "MatchAccepted", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SourceItems", "UseAbsoluteAnimeMapping", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SourceItems", "LinkedPath", "TEXT NULL");
        EnsureColumn(connection, "SourceItems", "AutoTorrentLinkKind", "INTEGER NULL");
        EnsureColumn(connection, "SourceItems", "AutoTorrentTorrentHash", "TEXT NULL");
        EnsureColumn(connection, "SourceItems", "AutoTorrentPackOwnerSeasonNumber", "INTEGER NULL");
        EnsureColumn(connection, "SourceItems", "IsExternalImport", "INTEGER NOT NULL DEFAULT 0");
        InitializeSeriesMappings(connection);
        InitializeTrackedShows(connection);
        InitializeTrackedMovies(connection);
        InitializeFetchJobs(connection);
        InitializeTorrentCartOrders(connection);
        InitializeTorrentCartOrderCandidates(connection);
        _logger.Info("SQLite database is ready", LogTarget.File | LogTarget.Ui | LogTarget.Console);
    }

    public IReadOnlyList<SourceItem> GetSourceItems()
    {
        var results = new List<SourceItem>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceRootFolder, ParentFolder, FilePath, FileName, ScanText, MediaKind, ParserPattern,
                   ShowTitle, MovieTitle, MovieYear, SeasonNumber, EpisodeNumber,
                   MappedSeasonNumber, MappedEpisodeNumber, EpisodeMappingSource, EpisodeMappingConfidence, EpisodeMappingReason,
                   EpisodeTitle,
                   MatchedTitle, MatchedYear, Provider, ProviderId, MatchConfidence, MatchReason,
                   RequiresManualReview, MatchAccepted, UseAbsoluteAnimeMapping,
                   State, Notes, LinkedPath, AutoTorrentLinkKind, AutoTorrentTorrentHash,
                   AutoTorrentPackOwnerSeasonNumber, IsExternalImport, LastSeenUtc
            FROM SourceItems
            ORDER BY LastSeenUtc DESC;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadItem(reader));
        }

        _logger.Debug($"Loaded {results.Count} source item(s) from SQLite", LogTarget.File | LogTarget.Console);
        return results;
    }

    public void UpsertSourceItem(SourceItem item)
    {
        UpsertSourceItem(item, preserveLinkedState: true);
    }

    public void UpdateSourceItem(SourceItem item)
    {
        UpsertSourceItem(item, preserveLinkedState: false);
    }

    private void UpsertSourceItem(SourceItem item, bool preserveLinkedState)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        var stateUpdateSql = preserveLinkedState
            ? "State = CASE WHEN SourceItems.State IN (3, 6) THEN SourceItems.State ELSE excluded.State END,"
            : "State = excluded.State,";
        var linkedPathUpdateSql = preserveLinkedState
            ? "LinkedPath = COALESCE(excluded.LinkedPath, SourceItems.LinkedPath),"
            : "LinkedPath = excluded.LinkedPath,";
        var autoTorrentMetadataUpdateSql = preserveLinkedState
            ? """
                AutoTorrentLinkKind = COALESCE(excluded.AutoTorrentLinkKind, SourceItems.AutoTorrentLinkKind),
                AutoTorrentTorrentHash = COALESCE(excluded.AutoTorrentTorrentHash, SourceItems.AutoTorrentTorrentHash),
                AutoTorrentPackOwnerSeasonNumber = COALESCE(excluded.AutoTorrentPackOwnerSeasonNumber, SourceItems.AutoTorrentPackOwnerSeasonNumber),
                """
            : """
                AutoTorrentLinkKind = excluded.AutoTorrentLinkKind,
                AutoTorrentTorrentHash = excluded.AutoTorrentTorrentHash,
                AutoTorrentPackOwnerSeasonNumber = excluded.AutoTorrentPackOwnerSeasonNumber,
                """;
        var externalImportUpdateSql = preserveLinkedState
            ? "IsExternalImport = CASE WHEN SourceItems.State IN (3, 6) THEN SourceItems.IsExternalImport ELSE excluded.IsExternalImport END,"
            : "IsExternalImport = excluded.IsExternalImport,";

        command.CommandText = $"""
            INSERT INTO SourceItems (SourceRootFolder, ParentFolder, FilePath, FileName, ScanText, MediaKind, ParserPattern, ShowTitle, MovieTitle, MovieYear, SeasonNumber, EpisodeNumber, MappedSeasonNumber, MappedEpisodeNumber, EpisodeMappingSource, EpisodeMappingConfidence, EpisodeMappingReason, EpisodeTitle, MatchedTitle, MatchedYear, Provider, ProviderId, MatchConfidence, MatchReason, RequiresManualReview, MatchAccepted, UseAbsoluteAnimeMapping, State, Notes, LinkedPath, AutoTorrentLinkKind, AutoTorrentTorrentHash, AutoTorrentPackOwnerSeasonNumber, IsExternalImport, LastSeenUtc)
            VALUES ($SourceRootFolder, $ParentFolder, $FilePath, $FileName, $ScanText, $MediaKind, $ParserPattern, $ShowTitle, $MovieTitle, $MovieYear, $SeasonNumber, $EpisodeNumber, $MappedSeasonNumber, $MappedEpisodeNumber, $EpisodeMappingSource, $EpisodeMappingConfidence, $EpisodeMappingReason, $EpisodeTitle, $MatchedTitle, $MatchedYear, $Provider, $ProviderId, $MatchConfidence, $MatchReason, $RequiresManualReview, $MatchAccepted, $UseAbsoluteAnimeMapping, $State, $Notes, $LinkedPath, $AutoTorrentLinkKind, $AutoTorrentTorrentHash, $AutoTorrentPackOwnerSeasonNumber, $IsExternalImport, $LastSeenUtc)
            ON CONFLICT(FilePath) DO UPDATE SET
                SourceRootFolder = excluded.SourceRootFolder,
                ParentFolder = excluded.ParentFolder,
                FileName = excluded.FileName,
                ScanText = excluded.ScanText,
                MediaKind = excluded.MediaKind,
                ParserPattern = excluded.ParserPattern,
                ShowTitle = excluded.ShowTitle,
                MovieTitle = excluded.MovieTitle,
                MovieYear = excluded.MovieYear,
                SeasonNumber = excluded.SeasonNumber,
                EpisodeNumber = excluded.EpisodeNumber,
                MappedSeasonNumber = excluded.MappedSeasonNumber,
                MappedEpisodeNumber = excluded.MappedEpisodeNumber,
                EpisodeMappingSource = excluded.EpisodeMappingSource,
                EpisodeMappingConfidence = excluded.EpisodeMappingConfidence,
                EpisodeMappingReason = excluded.EpisodeMappingReason,
                EpisodeTitle = excluded.EpisodeTitle,
                MatchedTitle = excluded.MatchedTitle,
                MatchedYear = excluded.MatchedYear,
                Provider = excluded.Provider,
                ProviderId = excluded.ProviderId,
                MatchConfidence = excluded.MatchConfidence,
                MatchReason = excluded.MatchReason,
                RequiresManualReview = excluded.RequiresManualReview,
                MatchAccepted = excluded.MatchAccepted,
                UseAbsoluteAnimeMapping = excluded.UseAbsoluteAnimeMapping,
                {stateUpdateSql}
                Notes = excluded.Notes,
                {linkedPathUpdateSql}
                {autoTorrentMetadataUpdateSql}
                {externalImportUpdateSql}
                LastSeenUtc = excluded.LastSeenUtc;
            """;
        AddParameters(command, item);
        command.ExecuteNonQuery();
    }

    public void UpsertSourceItems(IEnumerable<SourceItem> items)
    {
        var count = 0;
        foreach (var item in items)
        {
            UpsertSourceItem(item, preserveLinkedState: true);
            count++;
        }

        _logger.Info($"Persisted {count} source item(s)", LogTarget.File | LogTarget.Console);
    }

    public int MarkMissingSourceItems(IEnumerable<string> sourceFolders, IEnumerable<string> seenFilePaths)
    {
        var roots = sourceFolders
            .Where(Directory.Exists)
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = seenFilePaths
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (roots.Count == 0)
        {
            return 0;
        }

        var missingCount = 0;
        foreach (var item in GetSourceItems())
        {
            if (!roots.Contains(NormalizePath(item.SourceRootFolder)) ||
                seen.Contains(NormalizePath(item.FilePath)) ||
                item.State is ItemState.Deleted or ItemState.Ignored)
            {
                continue;
            }

            item.State = ItemState.Deleted;
            item.Notes = "Source file was not found during the latest scan.";
            item.LastSeenUtc = DateTime.UtcNow;
            UpdateSourceItem(item);
            missingCount++;
        }

        if (missingCount > 0)
        {
            _logger.Warning($"Marked {missingCount} source item(s) as deleted because their source files were not found", LogTarget.All);
        }

        return missingCount;
    }

    public int DeleteSourceItemsByState(ItemState state)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SourceItems WHERE State = $State;";
        command.Parameters.AddWithValue("$State", (int)state);
        var deletedCount = command.ExecuteNonQuery();

        _logger.Info($"Deleted {deletedCount} item(s) with state {state} from SQLite", LogTarget.All);
        return deletedCount;
    }

    public int DeleteSourceItem(long id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SourceItems WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id);
        var deletedCount = command.ExecuteNonQuery();

        if (deletedCount > 0)
        {
            _logger.Info($"Deleted stale source item id={id} from SQLite", LogTarget.All);
        }

        return deletedCount;
    }

    private static void AddParameters(SqliteCommand command, SourceItem item)
    {
        command.Parameters.AddWithValue("$SourceRootFolder", item.SourceRootFolder);
        command.Parameters.AddWithValue("$ParentFolder", item.ParentFolder);
        command.Parameters.AddWithValue("$FilePath", item.FilePath);
        command.Parameters.AddWithValue("$FileName", item.FileName);
        command.Parameters.AddWithValue("$ScanText", item.ScanText);
        command.Parameters.AddWithValue("$MediaKind", (int)item.MediaKind);
        command.Parameters.AddWithValue("$ParserPattern", (int)item.ParserPattern);
        command.Parameters.AddWithValue("$ShowTitle", (object?)item.ShowTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$MovieTitle", (object?)item.MovieTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$MovieYear", (object?)item.MovieYear ?? DBNull.Value);
        command.Parameters.AddWithValue("$SeasonNumber", (object?)item.SeasonNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$EpisodeNumber", (object?)item.EpisodeNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$MappedSeasonNumber", (object?)item.MappedSeasonNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$MappedEpisodeNumber", (object?)item.MappedEpisodeNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$EpisodeMappingSource", (object?)item.EpisodeMappingSource ?? DBNull.Value);
        command.Parameters.AddWithValue("$EpisodeMappingConfidence", (object?)item.EpisodeMappingConfidence ?? DBNull.Value);
        command.Parameters.AddWithValue("$EpisodeMappingReason", (object?)item.EpisodeMappingReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$EpisodeTitle", (object?)item.EpisodeTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$MatchedTitle", (object?)item.MatchedTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$MatchedYear", (object?)item.MatchedYear ?? DBNull.Value);
        command.Parameters.AddWithValue("$Provider", (object?)item.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue("$ProviderId", (object?)item.ProviderId ?? DBNull.Value);
        command.Parameters.AddWithValue("$MatchConfidence", (object?)item.MatchConfidence ?? DBNull.Value);
        command.Parameters.AddWithValue("$MatchReason", (object?)item.MatchReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$RequiresManualReview", item.RequiresManualReview ? 1 : 0);
        command.Parameters.AddWithValue("$MatchAccepted", item.MatchAccepted ? 1 : 0);
        command.Parameters.AddWithValue("$UseAbsoluteAnimeMapping", item.UseAbsoluteAnimeMapping ? 1 : 0);
        command.Parameters.AddWithValue("$State", (int)item.State);
        command.Parameters.AddWithValue("$Notes", (object?)item.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$LinkedPath", (object?)item.LinkedPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$AutoTorrentLinkKind", item.AutoTorrentLinkKind is null ? DBNull.Value : (int)item.AutoTorrentLinkKind.Value);
        command.Parameters.AddWithValue("$AutoTorrentTorrentHash", (object?)item.AutoTorrentTorrentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$AutoTorrentPackOwnerSeasonNumber", (object?)item.AutoTorrentPackOwnerSeasonNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$IsExternalImport", item.IsExternalImport ? 1 : 0);
        command.Parameters.AddWithValue("$LastSeenUtc", item.LastSeenUtc.ToString("O"));
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }

    private static SourceItem ReadItem(SqliteDataReader reader)
    {
        return new SourceItem
        {
            Id = reader.GetInt64(0),
            SourceRootFolder = reader.GetString(1),
            ParentFolder = reader.GetString(2),
            FilePath = reader.GetString(3),
            FileName = reader.GetString(4),
            ScanText = reader.GetString(5),
            MediaKind = (MediaKind)reader.GetInt32(6),
            ParserPattern = (ParserPattern)reader.GetInt32(7),
            ShowTitle = reader.IsDBNull(8) ? null : reader.GetString(8),
            MovieTitle = reader.IsDBNull(9) ? null : reader.GetString(9),
            MovieYear = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            SeasonNumber = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            EpisodeNumber = reader.IsDBNull(12) ? null : reader.GetInt32(12),
            MappedSeasonNumber = reader.IsDBNull(13) ? null : reader.GetInt32(13),
            MappedEpisodeNumber = reader.IsDBNull(14) ? null : reader.GetInt32(14),
            EpisodeMappingSource = reader.IsDBNull(15) ? null : reader.GetString(15),
            EpisodeMappingConfidence = reader.IsDBNull(16) ? null : reader.GetDouble(16),
            EpisodeMappingReason = reader.IsDBNull(17) ? null : reader.GetString(17),
            EpisodeTitle = reader.IsDBNull(18) ? null : reader.GetString(18),
            MatchedTitle = reader.IsDBNull(19) ? null : reader.GetString(19),
            MatchedYear = reader.IsDBNull(20) ? null : reader.GetInt32(20),
            Provider = reader.IsDBNull(21) ? null : reader.GetString(21),
            ProviderId = reader.IsDBNull(22) ? null : reader.GetString(22),
            MatchConfidence = reader.IsDBNull(23) ? null : reader.GetDouble(23),
            MatchReason = reader.IsDBNull(24) ? null : reader.GetString(24),
            RequiresManualReview = reader.GetInt32(25) == 1,
            MatchAccepted = reader.GetInt32(26) == 1,
            UseAbsoluteAnimeMapping = reader.GetInt32(27) == 1,
            State = (ItemState)reader.GetInt32(28),
            Notes = reader.IsDBNull(29) ? null : reader.GetString(29),
            LinkedPath = reader.IsDBNull(30) ? null : reader.GetString(30),
            AutoTorrentLinkKind = reader.IsDBNull(31) ? null : (AutoTorrentLinkKind)reader.GetInt32(31),
            AutoTorrentTorrentHash = reader.IsDBNull(32) ? null : reader.GetString(32),
            AutoTorrentPackOwnerSeasonNumber = reader.IsDBNull(33) ? null : reader.GetInt32(33),
            IsExternalImport = reader.GetInt32(34) == 1,
            LastSeenUtc = DateTime.Parse(reader.GetString(35), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    public SeriesMapping? GetSeriesMapping(string parsedTitle, ParserPattern parserPattern)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ParsedTitle, ParserPattern, MatchedTitle, MatchedYear, Provider, ProviderId, UseAbsoluteAnimeMapping, CreatedUtc, UpdatedUtc
            FROM SeriesMappings
            WHERE NormalizedParsedTitle = $NormalizedParsedTitle AND ParserPattern = $ParserPattern
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$NormalizedParsedTitle", NormalizeTitleKey(parsedTitle));
        command.Parameters.AddWithValue("$ParserPattern", (int)parserPattern);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new SeriesMapping
        {
            Id = reader.GetInt64(0),
            ParsedTitle = reader.GetString(1),
            ParserPattern = (ParserPattern)reader.GetInt32(2),
            MatchedTitle = reader.GetString(3),
            MatchedYear = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Provider = reader.GetString(5),
            ProviderId = reader.GetString(6),
            UseAbsoluteAnimeMapping = reader.GetInt32(7) == 1,
            CreatedUtc = DateTime.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedUtc = DateTime.Parse(reader.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    public void UpsertSeriesMapping(SeriesMapping mapping)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SeriesMappings (ParsedTitle, NormalizedParsedTitle, ParserPattern, MatchedTitle, MatchedYear, Provider, ProviderId, UseAbsoluteAnimeMapping, CreatedUtc, UpdatedUtc)
            VALUES ($ParsedTitle, $NormalizedParsedTitle, $ParserPattern, $MatchedTitle, $MatchedYear, $Provider, $ProviderId, $UseAbsoluteAnimeMapping, $CreatedUtc, $UpdatedUtc)
            ON CONFLICT(NormalizedParsedTitle, ParserPattern) DO UPDATE SET
                ParsedTitle = excluded.ParsedTitle,
                MatchedTitle = excluded.MatchedTitle,
                MatchedYear = excluded.MatchedYear,
                Provider = excluded.Provider,
                ProviderId = excluded.ProviderId,
                UseAbsoluteAnimeMapping = excluded.UseAbsoluteAnimeMapping,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        var now = DateTime.UtcNow;
        command.Parameters.AddWithValue("$ParsedTitle", mapping.ParsedTitle);
        command.Parameters.AddWithValue("$NormalizedParsedTitle", NormalizeTitleKey(mapping.ParsedTitle));
        command.Parameters.AddWithValue("$ParserPattern", (int)mapping.ParserPattern);
        command.Parameters.AddWithValue("$MatchedTitle", mapping.MatchedTitle);
        command.Parameters.AddWithValue("$MatchedYear", (object?)mapping.MatchedYear ?? DBNull.Value);
        command.Parameters.AddWithValue("$Provider", mapping.Provider);
        command.Parameters.AddWithValue("$ProviderId", mapping.ProviderId);
        command.Parameters.AddWithValue("$UseAbsoluteAnimeMapping", mapping.UseAbsoluteAnimeMapping ? 1 : 0);
        command.Parameters.AddWithValue("$CreatedUtc", (mapping.CreatedUtc == default ? now : mapping.CreatedUtc).ToString("O"));
        command.Parameters.AddWithValue("$UpdatedUtc", now.ToString("O"));
        command.ExecuteNonQuery();
        _logger.Info($"Saved series mapping: {mapping.ParsedTitle} => {mapping.MatchedTitle} [{mapping.Provider}-{mapping.ProviderId}]", LogTarget.All);
    }

    public IReadOnlyList<TrackedShow> GetTrackedShows()
    {
        var shows = new List<TrackedShow>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.TmdbId, s.Title, s.FirstAirYear, s.Overview, s.PosterPath, s.RecipeId, s.PackRecipeId, s.PreferredQuality, s.PreferredAudioCodec, s.MinimumSeeders, s.CreatedUtc, s.UpdatedUtc,
                   COUNT(e.Id), SUM(CASE WHEN e.Availability = 1 THEN 1 ELSE 0 END), SUM(CASE WHEN e.IsWanted = 1 THEN 1 ELSE 0 END)
            FROM TrackedShows s
            LEFT JOIN TrackedEpisodes e ON e.ShowId = s.Id
            GROUP BY s.Id
            ORDER BY s.Title;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            shows.Add(ReadTrackedShow(reader));
        }

        return shows;
    }

    public TrackedShow? GetTrackedShow(long id)
    {
        return GetTrackedShowCore("s.Id = $Value", id);
    }

    public TrackedShow? GetTrackedShowByTmdbId(int tmdbId)
    {
        return GetTrackedShowCore("s.TmdbId = $Value", tmdbId);
    }

    public long UpsertTrackedShow(TrackedShow show)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var now = DateTime.UtcNow;
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TrackedShows (TmdbId, Title, FirstAirYear, Overview, PosterPath, RecipeId, PackRecipeId, PreferredQuality, PreferredAudioCodec, MinimumSeeders, CreatedUtc, UpdatedUtc)
            VALUES ($TmdbId, $Title, $FirstAirYear, $Overview, $PosterPath, $RecipeId, $PackRecipeId, $PreferredQuality, $PreferredAudioCodec, $MinimumSeeders, $CreatedUtc, $UpdatedUtc)
            ON CONFLICT(TmdbId) DO UPDATE SET
                Title = excluded.Title,
                FirstAirYear = excluded.FirstAirYear,
                Overview = excluded.Overview,
                PosterPath = excluded.PosterPath,
                RecipeId = COALESCE(TrackedShows.RecipeId, excluded.RecipeId),
                PackRecipeId = COALESCE(TrackedShows.PackRecipeId, excluded.PackRecipeId),
                PreferredQuality = CASE WHEN TrackedShows.PreferredQuality = '' THEN excluded.PreferredQuality ELSE TrackedShows.PreferredQuality END,
                PreferredAudioCodec = TrackedShows.PreferredAudioCodec,
                MinimumSeeders = TrackedShows.MinimumSeeders,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$TmdbId", show.TmdbId);
        command.Parameters.AddWithValue("$Title", show.Title);
        command.Parameters.AddWithValue("$FirstAirYear", (object?)show.FirstAirYear ?? DBNull.Value);
        command.Parameters.AddWithValue("$Overview", (object?)show.Overview ?? DBNull.Value);
        command.Parameters.AddWithValue("$PosterPath", (object?)show.PosterPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$RecipeId", (object?)show.RecipeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$PackRecipeId", (object?)show.PackRecipeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$PreferredQuality", string.IsNullOrWhiteSpace(show.PreferredQuality) ? "1080p" : show.PreferredQuality);
        command.Parameters.AddWithValue("$PreferredAudioCodec", (object?)show.PreferredAudioCodec?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$MinimumSeeders", Math.Max(0, show.MinimumSeeders));
        command.Parameters.AddWithValue("$CreatedUtc", (show.CreatedUtc == default ? now : show.CreatedUtc).ToString("O"));
        command.Parameters.AddWithValue("$UpdatedUtc", now.ToString("O"));
        command.ExecuteNonQuery();

        using var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT Id FROM TrackedShows WHERE TmdbId = $TmdbId;";
        idCommand.Parameters.AddWithValue("$TmdbId", show.TmdbId);
        return (long)(idCommand.ExecuteScalar() ?? 0L);
    }

    public void DeleteTrackedSeasonsAndEpisodes(long showId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM TrackedEpisodes WHERE ShowId = $ShowId;
            DELETE FROM TrackedSeasons WHERE ShowId = $ShowId;
            """;
        command.Parameters.AddWithValue("$ShowId", showId);
        command.ExecuteNonQuery();
    }

    public void DeleteTrackedShow(long showId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TrackedShows WHERE Id = $ShowId;";
        command.Parameters.AddWithValue("$ShowId", showId);
        command.ExecuteNonQuery();
    }

    public int DeleteAllTrackedShows()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TrackedShows;";
        return command.ExecuteNonQuery();
    }

    public void DeleteTrackedMovie(long movieId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TrackedMovies WHERE Id = $MovieId;";
        command.Parameters.AddWithValue("$MovieId", movieId);
        command.ExecuteNonQuery();
    }

    public int DeleteAllTrackedMovies()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TrackedMovies;";
        return command.ExecuteNonQuery();
    }

    public int DeleteFetchJobsForMedia(long mediaId, MediaKind targetKind)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM FetchJobs
            WHERE ShowId = $MediaId AND TargetKind = $TargetKind;
            """;
        command.Parameters.AddWithValue("$MediaId", mediaId);
        command.Parameters.AddWithValue("$TargetKind", (int)targetKind);
        return command.ExecuteNonQuery();
    }

    public int DeleteAllFetchJobs()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FetchJobs;";
        return command.ExecuteNonQuery();
    }

    public void UpsertTrackedSeason(TrackedSeason season)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TrackedSeasons (ShowId, SeasonNumber, EpisodeCount, DownloadFolder, ManagementMode)
            VALUES ($ShowId, $SeasonNumber, $EpisodeCount, $DownloadFolder, $ManagementMode)
            ON CONFLICT(ShowId, SeasonNumber) DO UPDATE SET
                EpisodeCount = excluded.EpisodeCount,
                DownloadFolder = COALESCE(TrackedSeasons.DownloadFolder, excluded.DownloadFolder),
                ManagementMode = TrackedSeasons.ManagementMode,
                IsHidden = TrackedSeasons.IsHidden;
            """;
        command.Parameters.AddWithValue("$ShowId", season.ShowId);
        command.Parameters.AddWithValue("$SeasonNumber", season.SeasonNumber);
        command.Parameters.AddWithValue("$EpisodeCount", season.EpisodeCount);
        command.Parameters.AddWithValue("$DownloadFolder", (object?)season.DownloadFolder ?? DBNull.Value);
        command.Parameters.AddWithValue("$ManagementMode", (int)season.ManagementMode);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TrackedSeason> GetTrackedSeasons(long showId)
    {
        var seasons = new List<TrackedSeason>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ShowId, SeasonNumber, EpisodeCount, DownloadFolder, ManagementMode,
                   SelectedPackCandidateName, SelectedPackCandidateUrl, SelectedPackCandidatePlugin,
                   SelectedPackCandidateFileSize, SelectedPackCandidateSeeders, SelectedPackCandidateQuality,
                   SelectedPackCandidateAudioCodec, SelectedPackCoveredSeasons, SelectedPackOwnerSeasonNumber,
                   PackTorrentHash, PackTorrentName, PackTorrentState, PackTorrentProgress, IsHidden
            FROM TrackedSeasons
            WHERE ShowId = $ShowId
            ORDER BY SeasonNumber;
            """;
        command.Parameters.AddWithValue("$ShowId", showId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            seasons.Add(new TrackedSeason
            {
                Id = reader.GetInt64(0),
                ShowId = reader.GetInt64(1),
                SeasonNumber = reader.GetInt32(2),
                EpisodeCount = reader.GetInt32(3),
                DownloadFolder = reader.IsDBNull(4) ? null : reader.GetString(4),
                ManagementMode = (SeasonManagementMode)reader.GetInt32(5),
                SelectedPackCandidateName = reader.IsDBNull(6) ? null : reader.GetString(6),
                SelectedPackCandidateUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
                SelectedPackCandidatePlugin = reader.IsDBNull(8) ? null : reader.GetString(8),
                SelectedPackCandidateFileSize = reader.GetInt64(9),
                SelectedPackCandidateSeeders = reader.GetInt32(10),
                SelectedPackCandidateQuality = reader.IsDBNull(11) ? null : reader.GetString(11),
                SelectedPackCandidateAudioCodec = reader.IsDBNull(12) ? null : reader.GetString(12),
                SelectedPackCoveredSeasons = reader.IsDBNull(13) ? null : reader.GetString(13),
                SelectedPackOwnerSeasonNumber = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                PackTorrentHash = reader.IsDBNull(15) ? null : reader.GetString(15),
                PackTorrentName = reader.IsDBNull(16) ? null : reader.GetString(16),
                PackTorrentState = reader.IsDBNull(17) ? null : reader.GetString(17),
                PackTorrentProgress = reader.GetDouble(18),
                IsHidden = !reader.IsDBNull(19) && reader.GetInt32(19) != 0
            });
        }

        return seasons;
    }

    public void UpdateTrackedSeasonHidden(long showId, int seasonNumber, bool isHidden)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TrackedSeasons (ShowId, SeasonNumber, EpisodeCount, IsHidden)
            VALUES ($ShowId, $SeasonNumber, 0, $IsHidden)
            ON CONFLICT(ShowId, SeasonNumber) DO UPDATE SET
                IsHidden = excluded.IsHidden;
            """;
        command.Parameters.AddWithValue("$IsHidden", isHidden ? 1 : 0);
        command.Parameters.AddWithValue("$ShowId", showId);
        command.Parameters.AddWithValue("$SeasonNumber", seasonNumber);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedSeasonDownloadFolder(long showId, int seasonNumber, string? downloadFolder)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedSeasons
            SET DownloadFolder = $DownloadFolder
            WHERE ShowId = $ShowId AND SeasonNumber = $SeasonNumber;
            """;
        command.Parameters.AddWithValue("$DownloadFolder", string.IsNullOrWhiteSpace(downloadFolder) ? DBNull.Value : downloadFolder.Trim());
        command.Parameters.AddWithValue("$ShowId", showId);
        command.Parameters.AddWithValue("$SeasonNumber", seasonNumber);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedSeasonPackMode(long showId, int seasonNumber, SeasonManagementMode mode)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedSeasons
            SET ManagementMode = $ManagementMode
            WHERE ShowId = $ShowId AND SeasonNumber = $SeasonNumber;
            """;
        command.Parameters.AddWithValue("$ManagementMode", (int)mode);
        command.Parameters.AddWithValue("$ShowId", showId);
        command.Parameters.AddWithValue("$SeasonNumber", seasonNumber);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedSeasonSelectedPack(long showId, int ownerSeasonNumber, SeasonPackCandidate candidate)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var coveredSeasons = string.Join(",", candidate.CoveredSeasons);

        foreach (var seasonNumber in candidate.CoveredSeasons.DefaultIfEmpty(ownerSeasonNumber))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE TrackedSeasons
                SET SelectedPackCandidateName = $Name,
                    SelectedPackCandidateUrl = $Url,
                    SelectedPackCandidatePlugin = $Plugin,
                    SelectedPackCandidateFileSize = $FileSize,
                    SelectedPackCandidateSeeders = $Seeders,
                    SelectedPackCandidateQuality = $Quality,
                    SelectedPackCandidateAudioCodec = $AudioCodec,
                    SelectedPackCoveredSeasons = $CoveredSeasons,
                    SelectedPackOwnerSeasonNumber = $OwnerSeasonNumber
                WHERE ShowId = $ShowId AND SeasonNumber = $SeasonNumber;
                """;
            command.Parameters.AddWithValue("$Name", candidate.FileName);
            command.Parameters.AddWithValue("$Url", candidate.FileUrl);
            command.Parameters.AddWithValue("$Plugin", candidate.PluginName);
            command.Parameters.AddWithValue("$FileSize", candidate.FileSize);
            command.Parameters.AddWithValue("$Seeders", candidate.Seeders);
            command.Parameters.AddWithValue("$Quality", string.IsNullOrWhiteSpace(candidate.QualityLabel) ? DBNull.Value : candidate.QualityLabel);
            command.Parameters.AddWithValue("$AudioCodec", string.IsNullOrWhiteSpace(candidate.AudioCodecLabel) ? DBNull.Value : candidate.AudioCodecLabel);
            command.Parameters.AddWithValue("$CoveredSeasons", coveredSeasons);
            command.Parameters.AddWithValue("$OwnerSeasonNumber", ownerSeasonNumber);
            command.Parameters.AddWithValue("$ShowId", showId);
            command.Parameters.AddWithValue("$SeasonNumber", seasonNumber);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void ClearTrackedSeasonSelectedPacksForSeasons(long showId, IReadOnlyList<int> seasonNumbers)
    {
        var seasons = seasonNumbers.Where(season => season > 0).Distinct().ToList();
        if (seasons.Count == 0)
        {
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var ownerNumbers = new HashSet<int>();
        foreach (var seasonNumber in seasons)
        {
            using var selectCommand = connection.CreateCommand();
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = """
                SELECT SelectedPackOwnerSeasonNumber
                FROM TrackedSeasons
                WHERE ShowId = $ShowId
                  AND SeasonNumber = $SeasonNumber
                  AND SelectedPackOwnerSeasonNumber IS NOT NULL;
                """;
            selectCommand.Parameters.AddWithValue("$ShowId", showId);
            selectCommand.Parameters.AddWithValue("$SeasonNumber", seasonNumber);
            var ownerValue = selectCommand.ExecuteScalar();
            if (ownerValue is not null && ownerValue != DBNull.Value && int.TryParse(ownerValue.ToString(), out var ownerNumber))
            {
                ownerNumbers.Add(ownerNumber);
            }
        }

        if (ownerNumbers.Count == 0)
        {
            transaction.Commit();
            return;
        }

        foreach (var ownerNumber in ownerNumbers)
        {
            using var clearCommand = connection.CreateCommand();
            clearCommand.Transaction = transaction;
            clearCommand.CommandText = """
                UPDATE TrackedSeasons
                SET SelectedPackCandidateName = NULL,
                    SelectedPackCandidateUrl = NULL,
                    SelectedPackCandidatePlugin = NULL,
                    SelectedPackCandidateFileSize = 0,
                    SelectedPackCandidateSeeders = 0,
                    SelectedPackCandidateQuality = NULL,
                    SelectedPackCandidateAudioCodec = NULL,
                    SelectedPackCoveredSeasons = NULL,
                    SelectedPackOwnerSeasonNumber = NULL,
                    PackTorrentHash = NULL,
                    PackTorrentName = NULL,
                    PackTorrentState = NULL,
                    PackTorrentProgress = 0
                WHERE ShowId = $ShowId AND SelectedPackOwnerSeasonNumber = $OwnerSeasonNumber;
                """;
            clearCommand.Parameters.AddWithValue("$ShowId", showId);
            clearCommand.Parameters.AddWithValue("$OwnerSeasonNumber", ownerNumber);
            clearCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void ClearTrackedSeasonSelectedPack(long showId, int ownerSeasonNumber)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedSeasons
            SET SelectedPackCandidateName = NULL,
                SelectedPackCandidateUrl = NULL,
                SelectedPackCandidatePlugin = NULL,
                SelectedPackCandidateFileSize = 0,
                SelectedPackCandidateSeeders = 0,
                SelectedPackCandidateQuality = NULL,
                SelectedPackCandidateAudioCodec = NULL,
                SelectedPackCoveredSeasons = NULL,
                SelectedPackOwnerSeasonNumber = NULL,
                PackTorrentHash = NULL,
                PackTorrentName = NULL,
                PackTorrentState = NULL,
                PackTorrentProgress = 0
            WHERE ShowId = $ShowId AND SelectedPackOwnerSeasonNumber = $OwnerSeasonNumber;
            """;
        command.Parameters.AddWithValue("$ShowId", showId);
        command.Parameters.AddWithValue("$OwnerSeasonNumber", ownerSeasonNumber);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedSeasonPackTorrent(long showId, int ownerSeasonNumber, AddedTorrentResult torrent)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedSeasons
            SET PackTorrentHash = $Hash,
                PackTorrentName = $Name,
                PackTorrentState = $State,
                PackTorrentProgress = $Progress
            WHERE ShowId = $ShowId AND SelectedPackOwnerSeasonNumber = $OwnerSeasonNumber;
            """;
        command.Parameters.AddWithValue("$Hash", torrent.Hash);
        command.Parameters.AddWithValue("$Name", torrent.Name);
        command.Parameters.AddWithValue("$State", torrent.IsComplete ? "Downloaded" : torrent.State);
        command.Parameters.AddWithValue("$Progress", Math.Clamp(torrent.Progress, 0, 1));
        command.Parameters.AddWithValue("$ShowId", showId);
        command.Parameters.AddWithValue("$OwnerSeasonNumber", ownerSeasonNumber);
        command.ExecuteNonQuery();
    }

    public void MarkTrackedSeasonPackTorrentRemoved(long showId, int ownerSeasonNumber, string torrentHash)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedSeasons
            SET PackTorrentHash = $Hash,
                PackTorrentName = '',
                PackTorrentState = 'Removed from qBittorrent',
                PackTorrentProgress = 0
            WHERE ShowId = $ShowId AND SelectedPackOwnerSeasonNumber = $OwnerSeasonNumber;
            """;
        command.Parameters.AddWithValue("$Hash", string.IsNullOrWhiteSpace(torrentHash) ? DBNull.Value : torrentHash.Trim());
        command.Parameters.AddWithValue("$ShowId", showId);
        command.Parameters.AddWithValue("$OwnerSeasonNumber", ownerSeasonNumber);
        command.ExecuteNonQuery();
    }

    public void UpsertTrackedEpisode(TrackedEpisode episode)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var now = DateTime.UtcNow;
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TrackedEpisodes (ShowId, SeasonNumber, EpisodeNumber, Title, AirDate, Availability, IsWanted, TorrentHash, TorrentName, TorrentState, TorrentProgress, TorrentUpdatedUtc, SelectedCandidateName, SelectedCandidateUrl, SelectedCandidatePlugin, SelectedCandidateFileSize, SelectedCandidateSeeders, SelectedCandidateQuality, SelectedCandidateAudioCodec, CreatedUtc, UpdatedUtc)
            VALUES ($ShowId, $SeasonNumber, $EpisodeNumber, $Title, $AirDate, $Availability, $IsWanted, $TorrentHash, $TorrentName, $TorrentState, $TorrentProgress, $TorrentUpdatedUtc, $SelectedCandidateName, $SelectedCandidateUrl, $SelectedCandidatePlugin, $SelectedCandidateFileSize, $SelectedCandidateSeeders, $SelectedCandidateQuality, $SelectedCandidateAudioCodec, $CreatedUtc, $UpdatedUtc)
            ON CONFLICT(ShowId, SeasonNumber, EpisodeNumber) DO UPDATE SET
                Title = excluded.Title,
                AirDate = excluded.AirDate,
                Availability = excluded.Availability,
                IsWanted = TrackedEpisodes.IsWanted,
                TorrentHash = TrackedEpisodes.TorrentHash,
                TorrentName = TrackedEpisodes.TorrentName,
                TorrentState = TrackedEpisodes.TorrentState,
                TorrentProgress = TrackedEpisodes.TorrentProgress,
                TorrentUpdatedUtc = TrackedEpisodes.TorrentUpdatedUtc,
                SelectedCandidateName = TrackedEpisodes.SelectedCandidateName,
                SelectedCandidateUrl = TrackedEpisodes.SelectedCandidateUrl,
                SelectedCandidatePlugin = TrackedEpisodes.SelectedCandidatePlugin,
                SelectedCandidateFileSize = TrackedEpisodes.SelectedCandidateFileSize,
                SelectedCandidateSeeders = TrackedEpisodes.SelectedCandidateSeeders,
                SelectedCandidateQuality = TrackedEpisodes.SelectedCandidateQuality,
                SelectedCandidateAudioCodec = TrackedEpisodes.SelectedCandidateAudioCodec,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$ShowId", episode.ShowId);
        command.Parameters.AddWithValue("$SeasonNumber", episode.SeasonNumber);
        command.Parameters.AddWithValue("$EpisodeNumber", episode.EpisodeNumber);
        command.Parameters.AddWithValue("$Title", episode.Title);
        command.Parameters.AddWithValue("$AirDate", (object?)episode.AirDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        command.Parameters.AddWithValue("$Availability", (int)episode.Availability);
        command.Parameters.AddWithValue("$IsWanted", episode.IsWanted ? 1 : 0);
        command.Parameters.AddWithValue("$TorrentHash", (object?)episode.TorrentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$TorrentName", (object?)episode.TorrentName ?? DBNull.Value);
        command.Parameters.AddWithValue("$TorrentState", (object?)episode.TorrentState ?? DBNull.Value);
        command.Parameters.AddWithValue("$TorrentProgress", episode.TorrentProgress);
        command.Parameters.AddWithValue("$TorrentUpdatedUtc", (object?)episode.TorrentUpdatedUtc?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$SelectedCandidateName", (object?)episode.SelectedCandidateName ?? DBNull.Value);
        command.Parameters.AddWithValue("$SelectedCandidateUrl", (object?)episode.SelectedCandidateUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$SelectedCandidatePlugin", (object?)episode.SelectedCandidatePlugin ?? DBNull.Value);
        command.Parameters.AddWithValue("$SelectedCandidateFileSize", episode.SelectedCandidateFileSize);
        command.Parameters.AddWithValue("$SelectedCandidateSeeders", episode.SelectedCandidateSeeders);
        command.Parameters.AddWithValue("$SelectedCandidateQuality", (object?)episode.SelectedCandidateQuality ?? DBNull.Value);
        command.Parameters.AddWithValue("$SelectedCandidateAudioCodec", (object?)episode.SelectedCandidateAudioCodec ?? DBNull.Value);
        command.Parameters.AddWithValue("$CreatedUtc", (episode.CreatedUtc == default ? now : episode.CreatedUtc).ToString("O"));
        command.Parameters.AddWithValue("$UpdatedUtc", now.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TrackedEpisode> GetTrackedEpisodes(long showId)
    {
        var episodes = new List<TrackedEpisode>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ShowId, SeasonNumber, EpisodeNumber, Title, AirDate, Availability, IsWanted,
                   TorrentHash, TorrentName, TorrentState, TorrentProgress, TorrentUpdatedUtc,
                   SelectedCandidateName, SelectedCandidateUrl, SelectedCandidatePlugin, SelectedCandidateFileSize,
                   SelectedCandidateSeeders, SelectedCandidateQuality, SelectedCandidateAudioCodec,
                   CreatedUtc, UpdatedUtc
            FROM TrackedEpisodes
            WHERE ShowId = $ShowId
            ORDER BY SeasonNumber, EpisodeNumber;
            """;
        command.Parameters.AddWithValue("$ShowId", showId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            episodes.Add(ReadTrackedEpisode(reader));
        }

        return episodes;
    }

    public void UpdateTrackedEpisodeWanted(long episodeId, bool isWanted)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE TrackedEpisodes SET IsWanted = $IsWanted, UpdatedUtc = $UpdatedUtc WHERE Id = $Id;";
        command.Parameters.AddWithValue("$IsWanted", isWanted ? 1 : 0);
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", episodeId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedEpisodeAvailability(long episodeId, EpisodeAvailability availability)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE TrackedEpisodes SET Availability = $Availability, UpdatedUtc = $UpdatedUtc WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Availability", (int)availability);
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", episodeId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedEpisodeTorrent(
        long episodeId,
        string torrentHash,
        string torrentName,
        string torrentState,
        double torrentProgress)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedEpisodes
            SET TorrentHash = $TorrentHash,
                TorrentName = $TorrentName,
                TorrentState = $TorrentState,
                TorrentProgress = $TorrentProgress,
                TorrentUpdatedUtc = $TorrentUpdatedUtc,
                UpdatedUtc = $UpdatedUtc
            WHERE Id = $Id;
            """;
        command.Parameters.AddWithValue("$TorrentHash", string.IsNullOrWhiteSpace(torrentHash) ? DBNull.Value : torrentHash.Trim());
        command.Parameters.AddWithValue("$TorrentName", string.IsNullOrWhiteSpace(torrentName) ? DBNull.Value : torrentName.Trim());
        command.Parameters.AddWithValue("$TorrentState", string.IsNullOrWhiteSpace(torrentState) ? DBNull.Value : torrentState.Trim());
        command.Parameters.AddWithValue("$TorrentProgress", Math.Clamp(torrentProgress, 0, 1));
        command.Parameters.AddWithValue("$TorrentUpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", episodeId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedEpisodeSelectedCandidate(long episodeId, EpisodeFetchCandidate candidate)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedEpisodes
            SET SelectedCandidateName = $Name,
                SelectedCandidateUrl = $Url,
                SelectedCandidatePlugin = $Plugin,
                SelectedCandidateFileSize = $FileSize,
                SelectedCandidateSeeders = $Seeders,
                SelectedCandidateQuality = $Quality,
                SelectedCandidateAudioCodec = $AudioCodec,
                UpdatedUtc = $UpdatedUtc
            WHERE Id = $Id;
            """;
        AddSelectedCandidateParameters(command, candidate);
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", episodeId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedShowPreferredQuality(long showId, string preferredQuality)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE TrackedShows SET PreferredQuality = $PreferredQuality, UpdatedUtc = $UpdatedUtc WHERE Id = $Id;";
        command.Parameters.AddWithValue("$PreferredQuality", string.IsNullOrWhiteSpace(preferredQuality) ? "1080p" : preferredQuality.Trim());
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", showId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedShowPreferences(long showId, string preferredQuality, string preferredAudioCodec, int minimumSeeders)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedShows
            SET PreferredQuality = $PreferredQuality,
                PreferredAudioCodec = $PreferredAudioCodec,
                MinimumSeeders = $MinimumSeeders,
                UpdatedUtc = $UpdatedUtc
            WHERE Id = $Id;
            """;
        command.Parameters.AddWithValue("$PreferredQuality", string.IsNullOrWhiteSpace(preferredQuality) ? "1080p" : preferredQuality.Trim());
        command.Parameters.AddWithValue("$PreferredAudioCodec", string.IsNullOrWhiteSpace(preferredAudioCodec) ? string.Empty : preferredAudioCodec.Trim());
        command.Parameters.AddWithValue("$MinimumSeeders", Math.Max(0, minimumSeeders));
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", showId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedShowRecipe(long showId, string? recipeId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE TrackedShows SET RecipeId = $RecipeId, UpdatedUtc = $UpdatedUtc WHERE Id = $Id;";
        command.Parameters.AddWithValue("$RecipeId", string.IsNullOrWhiteSpace(recipeId) ? DBNull.Value : recipeId.Trim());
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", showId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedShowPackRecipe(long showId, string? packRecipeId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE TrackedShows SET PackRecipeId = $PackRecipeId, UpdatedUtc = $UpdatedUtc WHERE Id = $Id;";
        command.Parameters.AddWithValue("$PackRecipeId", string.IsNullOrWhiteSpace(packRecipeId) ? DBNull.Value : packRecipeId.Trim());
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", showId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TrackedMovie> GetTrackedMovies()
    {
        var movies = new List<TrackedMovie>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TmdbId, Title, ReleaseYear, Overview, PosterPath, RecipeId, PreferredQuality, PreferredAudioCodec,
                   MinimumSeeders, Availability, IsWanted, TorrentHash, TorrentName, TorrentState,
                   TorrentProgress, TorrentUpdatedUtc, SelectedCandidateName, SelectedCandidateUrl,
                   SelectedCandidatePlugin, SelectedCandidateFileSize, SelectedCandidateSeeders,
                   SelectedCandidateQuality, SelectedCandidateAudioCodec, CreatedUtc, UpdatedUtc
            FROM TrackedMovies
            ORDER BY Title;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            movies.Add(ReadTrackedMovie(reader));
        }

        return movies;
    }

    public TrackedMovie? GetTrackedMovie(long id)
    {
        return GetTrackedMovieCore("Id = $Value", id);
    }

    public TrackedMovie? GetTrackedMovieByTmdbId(int tmdbId)
    {
        return GetTrackedMovieCore("TmdbId = $Value", tmdbId);
    }

    public long UpsertTrackedMovie(TrackedMovie movie)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var now = DateTime.UtcNow;
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TrackedMovies (TmdbId, Title, ReleaseYear, Overview, PosterPath, RecipeId, PreferredQuality, PreferredAudioCodec, MinimumSeeders, Availability, IsWanted, TorrentHash, TorrentName, TorrentState, TorrentProgress, TorrentUpdatedUtc, SelectedCandidateName, SelectedCandidateUrl, SelectedCandidatePlugin, SelectedCandidateFileSize, SelectedCandidateSeeders, SelectedCandidateQuality, SelectedCandidateAudioCodec, CreatedUtc, UpdatedUtc)
            VALUES ($TmdbId, $Title, $ReleaseYear, $Overview, $PosterPath, $RecipeId, $PreferredQuality, $PreferredAudioCodec, $MinimumSeeders, $Availability, $IsWanted, $TorrentHash, $TorrentName, $TorrentState, $TorrentProgress, $TorrentUpdatedUtc, $SelectedCandidateName, $SelectedCandidateUrl, $SelectedCandidatePlugin, $SelectedCandidateFileSize, $SelectedCandidateSeeders, $SelectedCandidateQuality, $SelectedCandidateAudioCodec, $CreatedUtc, $UpdatedUtc)
            ON CONFLICT(TmdbId) DO UPDATE SET
                Title = excluded.Title,
                ReleaseYear = excluded.ReleaseYear,
                Overview = excluded.Overview,
                PosterPath = excluded.PosterPath,
                RecipeId = COALESCE(TrackedMovies.RecipeId, excluded.RecipeId),
                PreferredQuality = TrackedMovies.PreferredQuality,
                PreferredAudioCodec = TrackedMovies.PreferredAudioCodec,
                MinimumSeeders = TrackedMovies.MinimumSeeders,
                Availability = TrackedMovies.Availability,
                IsWanted = TrackedMovies.IsWanted,
                TorrentHash = TrackedMovies.TorrentHash,
                TorrentName = TrackedMovies.TorrentName,
                TorrentState = TrackedMovies.TorrentState,
                TorrentProgress = TrackedMovies.TorrentProgress,
                TorrentUpdatedUtc = TrackedMovies.TorrentUpdatedUtc,
                SelectedCandidateName = TrackedMovies.SelectedCandidateName,
                SelectedCandidateUrl = TrackedMovies.SelectedCandidateUrl,
                SelectedCandidatePlugin = TrackedMovies.SelectedCandidatePlugin,
                SelectedCandidateFileSize = TrackedMovies.SelectedCandidateFileSize,
                SelectedCandidateSeeders = TrackedMovies.SelectedCandidateSeeders,
                SelectedCandidateQuality = TrackedMovies.SelectedCandidateQuality,
                SelectedCandidateAudioCodec = TrackedMovies.SelectedCandidateAudioCodec,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        AddTrackedMovieParameters(command, movie, now);
        command.ExecuteNonQuery();

        using var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT Id FROM TrackedMovies WHERE TmdbId = $TmdbId;";
        idCommand.Parameters.AddWithValue("$TmdbId", movie.TmdbId);
        return (long)(idCommand.ExecuteScalar() ?? 0L);
    }

    public void UpdateTrackedMovieWanted(long movieId, bool isWanted)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE TrackedMovies SET IsWanted = $IsWanted, UpdatedUtc = $UpdatedUtc WHERE Id = $Id;";
        command.Parameters.AddWithValue("$IsWanted", isWanted ? 1 : 0);
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", movieId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedMovieAvailability(long movieId, EpisodeAvailability availability)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE TrackedMovies SET Availability = $Availability, UpdatedUtc = $UpdatedUtc WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Availability", (int)availability);
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", movieId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedMovieTorrent(long movieId, string torrentHash, string torrentName, string torrentState, double torrentProgress)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedMovies
            SET TorrentHash = $TorrentHash,
                TorrentName = $TorrentName,
                TorrentState = $TorrentState,
                TorrentProgress = $TorrentProgress,
                TorrentUpdatedUtc = $TorrentUpdatedUtc,
                UpdatedUtc = $UpdatedUtc
            WHERE Id = $Id;
            """;
        command.Parameters.AddWithValue("$TorrentHash", string.IsNullOrWhiteSpace(torrentHash) ? DBNull.Value : torrentHash.Trim());
        command.Parameters.AddWithValue("$TorrentName", string.IsNullOrWhiteSpace(torrentName) ? DBNull.Value : torrentName.Trim());
        command.Parameters.AddWithValue("$TorrentState", string.IsNullOrWhiteSpace(torrentState) ? DBNull.Value : torrentState.Trim());
        command.Parameters.AddWithValue("$TorrentProgress", Math.Clamp(torrentProgress, 0, 1));
        command.Parameters.AddWithValue("$TorrentUpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", movieId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedMovieSelectedCandidate(long movieId, EpisodeFetchCandidate candidate)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedMovies
            SET SelectedCandidateName = $Name,
                SelectedCandidateUrl = $Url,
                SelectedCandidatePlugin = $Plugin,
                SelectedCandidateFileSize = $FileSize,
                SelectedCandidateSeeders = $Seeders,
                SelectedCandidateQuality = $Quality,
                SelectedCandidateAudioCodec = $AudioCodec,
                UpdatedUtc = $UpdatedUtc
            WHERE Id = $Id;
            """;
        AddSelectedCandidateParameters(command, candidate);
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", movieId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedMoviePreferences(long movieId, string preferredQuality, string preferredAudioCodec, int minimumSeeders)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedMovies
            SET PreferredQuality = $PreferredQuality,
                PreferredAudioCodec = $PreferredAudioCodec,
                MinimumSeeders = $MinimumSeeders,
                UpdatedUtc = $UpdatedUtc
            WHERE Id = $Id;
            """;
        command.Parameters.AddWithValue("$PreferredQuality", string.IsNullOrWhiteSpace(preferredQuality) ? "1080p" : preferredQuality.Trim());
        command.Parameters.AddWithValue("$PreferredAudioCodec", string.IsNullOrWhiteSpace(preferredAudioCodec) ? string.Empty : preferredAudioCodec.Trim());
        command.Parameters.AddWithValue("$MinimumSeeders", Math.Max(0, minimumSeeders));
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", movieId);
        command.ExecuteNonQuery();
    }

    public void UpdateTrackedMovieRecipe(long movieId, string? recipeId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE TrackedMovies SET RecipeId = $RecipeId, UpdatedUtc = $UpdatedUtc WHERE Id = $Id;";
        command.Parameters.AddWithValue("$RecipeId", string.IsNullOrWhiteSpace(recipeId) ? DBNull.Value : recipeId.Trim());
        command.Parameters.AddWithValue("$UpdatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$Id", movieId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TorrentCartOrder> GetTorrentCartOrders(MediaKind? mediaKind = null, long? mediaId = null)
    {
        var orders = new List<TorrentCartOrder>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        var whereClauses = new List<string>();
        if (mediaKind is not null)
        {
            whereClauses.Add("TargetKind = $TargetKind");
            command.Parameters.AddWithValue("$TargetKind", (int)mediaKind.Value);
        }

        if (mediaId is not null)
        {
            whereClauses.Add("MediaId = $MediaId");
            command.Parameters.AddWithValue("$MediaId", mediaId.Value);
        }

        command.CommandText = $"""
            SELECT Id, TargetKind, MediaId, EpisodeId, SeasonNumber, EpisodeNumber, Title, Summary, Status, StatusDetail,
                   SelectedCandidateName, SelectedCandidateUrl, SelectedCandidatePlugin, SelectedCandidateFileSize,
                   SelectedCandidateSeeders, SelectedCandidateLeechers, SelectedCandidateQuality, SelectedCandidateAudioCodec,
                   SelectedCandidateCoveredSeasons, SelectedCandidateTotalScore,
                   TorrentHash, TorrentName, TorrentState, TorrentProgress, CreatedUtc, UpdatedUtc
            FROM TorrentCartOrders
            {(whereClauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", whereClauses)}")}
            ORDER BY Id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            orders.Add(ReadTorrentCartOrder(reader));
        }

        return orders;
    }

    public TorrentCartOrder? GetTorrentCartOrder(long orderId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TargetKind, MediaId, EpisodeId, SeasonNumber, EpisodeNumber, Title, Summary, Status, StatusDetail,
                   SelectedCandidateName, SelectedCandidateUrl, SelectedCandidatePlugin, SelectedCandidateFileSize,
                   SelectedCandidateSeeders, SelectedCandidateLeechers, SelectedCandidateQuality, SelectedCandidateAudioCodec,
                   SelectedCandidateCoveredSeasons, SelectedCandidateTotalScore,
                   TorrentHash, TorrentName, TorrentState, TorrentProgress, CreatedUtc, UpdatedUtc
            FROM TorrentCartOrders
            WHERE Id = $Id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$Id", orderId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTorrentCartOrder(reader) : null;
    }

    public long UpsertTorrentCartOrder(TorrentCartOrder order)
    {
        order.UpdatedUtc = DateTime.UtcNow;
        if (order.Id <= 0)
        {
            order.CreatedUtc = order.UpdatedUtc;
            using var insertConnection = new SqliteConnection(_connectionString);
            insertConnection.Open();
            using var insert = insertConnection.CreateCommand();
            insert.CommandText = """
                INSERT INTO TorrentCartOrders (
                    TargetKind, MediaId, EpisodeId, SeasonNumber, EpisodeNumber, Title, Summary, Status, StatusDetail,
                    SelectedCandidateName, SelectedCandidateUrl, SelectedCandidatePlugin, SelectedCandidateFileSize,
                    SelectedCandidateSeeders, SelectedCandidateLeechers, SelectedCandidateQuality, SelectedCandidateAudioCodec,
                    SelectedCandidateCoveredSeasons, SelectedCandidateTotalScore,
                    TorrentHash, TorrentName, TorrentState, TorrentProgress, CreatedUtc, UpdatedUtc)
                VALUES (
                    $TargetKind, $MediaId, $EpisodeId, $SeasonNumber, $EpisodeNumber, $Title, $Summary, $Status, $StatusDetail,
                    $SelectedCandidateName, $SelectedCandidateUrl, $SelectedCandidatePlugin, $SelectedCandidateFileSize,
                    $SelectedCandidateSeeders, $SelectedCandidateLeechers, $SelectedCandidateQuality, $SelectedCandidateAudioCodec,
                    $SelectedCandidateCoveredSeasons, $SelectedCandidateTotalScore,
                    $TorrentHash, $TorrentName, $TorrentState, $TorrentProgress, $CreatedUtc, $UpdatedUtc);
                SELECT last_insert_rowid();
                """;
            AddTorrentCartOrderParameters(insert, order);
            order.Id = (long)(insert.ExecuteScalar() ?? 0L);
            return order.Id;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TorrentCartOrders (
                Id, TargetKind, MediaId, EpisodeId, SeasonNumber, EpisodeNumber, Title, Summary, Status, StatusDetail,
                SelectedCandidateName, SelectedCandidateUrl, SelectedCandidatePlugin, SelectedCandidateFileSize,
                SelectedCandidateSeeders, SelectedCandidateLeechers, SelectedCandidateQuality, SelectedCandidateAudioCodec,
                SelectedCandidateCoveredSeasons, SelectedCandidateTotalScore,
                TorrentHash, TorrentName, TorrentState, TorrentProgress, CreatedUtc, UpdatedUtc)
            VALUES (
                $Id, $TargetKind, $MediaId, $EpisodeId, $SeasonNumber, $EpisodeNumber, $Title, $Summary, $Status, $StatusDetail,
                $SelectedCandidateName, $SelectedCandidateUrl, $SelectedCandidatePlugin, $SelectedCandidateFileSize,
                $SelectedCandidateSeeders, $SelectedCandidateLeechers, $SelectedCandidateQuality, $SelectedCandidateAudioCodec,
                $SelectedCandidateCoveredSeasons, $SelectedCandidateTotalScore,
                $TorrentHash, $TorrentName, $TorrentState, $TorrentProgress, $CreatedUtc, $UpdatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                TargetKind = excluded.TargetKind,
                MediaId = excluded.MediaId,
                EpisodeId = excluded.EpisodeId,
                SeasonNumber = excluded.SeasonNumber,
                EpisodeNumber = excluded.EpisodeNumber,
                Title = excluded.Title,
                Summary = excluded.Summary,
                Status = excluded.Status,
                StatusDetail = excluded.StatusDetail,
                SelectedCandidateName = excluded.SelectedCandidateName,
                SelectedCandidateUrl = excluded.SelectedCandidateUrl,
                SelectedCandidatePlugin = excluded.SelectedCandidatePlugin,
                SelectedCandidateFileSize = excluded.SelectedCandidateFileSize,
                SelectedCandidateSeeders = excluded.SelectedCandidateSeeders,
                SelectedCandidateLeechers = excluded.SelectedCandidateLeechers,
                SelectedCandidateQuality = excluded.SelectedCandidateQuality,
                SelectedCandidateAudioCodec = excluded.SelectedCandidateAudioCodec,
                SelectedCandidateCoveredSeasons = excluded.SelectedCandidateCoveredSeasons,
                SelectedCandidateTotalScore = excluded.SelectedCandidateTotalScore,
                TorrentHash = excluded.TorrentHash,
                TorrentName = excluded.TorrentName,
                TorrentState = excluded.TorrentState,
                TorrentProgress = excluded.TorrentProgress,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$Id", order.Id);
        AddTorrentCartOrderParameters(command, order);
        command.ExecuteNonQuery();
        return order.Id;
    }

    public int DeleteTorrentCartOrders(MediaKind? mediaKind = null, long? mediaId = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var whereClauses = new List<string>();
        if (mediaKind is not null)
        {
            whereClauses.Add("TargetKind = $TargetKind");
        }

        if (mediaId is not null)
        {
            whereClauses.Add("MediaId = $MediaId");
        }

        var whereSql = whereClauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", whereClauses)}";
        using var deleteCandidates = connection.CreateCommand();
        deleteCandidates.CommandText = $"""
            DELETE FROM TorrentCartOrderCandidates
            WHERE OrderId IN (SELECT Id FROM TorrentCartOrders {whereSql});
            """;
        if (mediaKind is not null)
        {
            deleteCandidates.Parameters.AddWithValue("$TargetKind", (int)mediaKind.Value);
        }
        if (mediaId is not null)
        {
            deleteCandidates.Parameters.AddWithValue("$MediaId", mediaId.Value);
        }
        deleteCandidates.ExecuteNonQuery();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM TorrentCartOrders
            {whereSql};
            """;
        if (mediaKind is not null)
        {
            command.Parameters.AddWithValue("$TargetKind", (int)mediaKind.Value);
        }
        if (mediaId is not null)
        {
            command.Parameters.AddWithValue("$MediaId", mediaId.Value);
        }
        return command.ExecuteNonQuery();
    }

    public int DeleteTorrentCartOrder(long orderId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var deleteCandidates = connection.CreateCommand();
        deleteCandidates.CommandText = "DELETE FROM TorrentCartOrderCandidates WHERE OrderId = $Id;";
        deleteCandidates.Parameters.AddWithValue("$Id", orderId);
        deleteCandidates.ExecuteNonQuery();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TorrentCartOrders WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", orderId);
        return command.ExecuteNonQuery();
    }

    public int DeleteTorrentCartOrderCandidates(long orderId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TorrentCartOrderCandidates WHERE OrderId = $OrderId;";
        command.Parameters.AddWithValue("$OrderId", orderId);
        return command.ExecuteNonQuery();
    }

    public IReadOnlyList<TorrentCartOrderCandidate> GetTorrentCartOrderCandidates(long orderId)
    {
        var candidates = new List<TorrentCartOrderCandidate>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, OrderId, Rank, IsSelected, IsAccepted, Name, Url, PluginName, FileSize, Seeders, Leechers,
                   Quality, AudioCodec, CoveredSeasons, TotalScore
            FROM TorrentCartOrderCandidates
            WHERE OrderId = $OrderId
            ORDER BY Rank, Id;
            """;
        command.Parameters.AddWithValue("$OrderId", orderId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            candidates.Add(ReadTorrentCartOrderCandidate(reader));
        }

        return candidates;
    }

    public void ReplaceTorrentCartOrderCandidates(long orderId, IReadOnlyList<TorrentCartOrderCandidate> candidates)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM TorrentCartOrderCandidates WHERE OrderId = $OrderId;";
        delete.Parameters.AddWithValue("$OrderId", orderId);
        delete.ExecuteNonQuery();

        foreach (var candidate in candidates.Select((candidate, index) => (Candidate: candidate, Index: index)))
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO TorrentCartOrderCandidates (
                    OrderId, Rank, IsSelected, IsAccepted, Name, Url, PluginName, FileSize, Seeders, Leechers,
                    Quality, AudioCodec, CoveredSeasons, TotalScore)
                VALUES (
                    $OrderId, $Rank, $IsSelected, $IsAccepted, $Name, $Url, $PluginName, $FileSize, $Seeders, $Leechers,
                    $Quality, $AudioCodec, $CoveredSeasons, $TotalScore);
                """;
            AddTorrentCartOrderCandidateParameters(insert, orderId, candidate.Candidate, candidate.Index);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void UpdateTorrentCartOrderCandidateSelection(long orderId, long candidateId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TorrentCartOrderCandidates
            SET IsSelected = CASE WHEN Id = $CandidateId THEN 1 ELSE 0 END
            WHERE OrderId = $OrderId;
            """;
        command.Parameters.AddWithValue("$OrderId", orderId);
        command.Parameters.AddWithValue("$CandidateId", candidateId);
        command.ExecuteNonQuery();
    }

    public void UpdateTorrentCartOrderCandidateAccepted(long orderId, long candidateId, bool isAccepted)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TorrentCartOrderCandidates
            SET IsAccepted = CASE WHEN Id = $CandidateId THEN $IsAccepted ELSE 0 END
            WHERE OrderId = $OrderId;
            """;
        command.Parameters.AddWithValue("$OrderId", orderId);
        command.Parameters.AddWithValue("$CandidateId", candidateId);
        command.Parameters.AddWithValue("$IsAccepted", isAccepted ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public void ClearSelectedEpisodeCandidates()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedEpisodes
            SET SelectedCandidateName = NULL,
                SelectedCandidateUrl = NULL,
                SelectedCandidatePlugin = NULL,
                SelectedCandidateFileSize = 0,
                SelectedCandidateSeeders = 0,
                SelectedCandidateQuality = NULL,
                SelectedCandidateAudioCodec = NULL;
            """;
        command.ExecuteNonQuery();
    }

    public void ClearSelectedSeasonPackCandidates()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedSeasons
            SET SelectedPackCandidateName = NULL,
                SelectedPackCandidateUrl = NULL,
                SelectedPackCandidatePlugin = NULL,
                SelectedPackCandidateFileSize = 0,
                SelectedPackCandidateSeeders = 0,
                SelectedPackCandidateQuality = NULL,
                SelectedPackCandidateAudioCodec = NULL,
                SelectedPackCoveredSeasons = NULL,
                SelectedPackOwnerSeasonNumber = NULL,
                PackTorrentHash = NULL,
                PackTorrentName = NULL,
                PackTorrentState = NULL,
                PackTorrentProgress = 0;
            """;
        command.ExecuteNonQuery();
    }

    public void ClearSelectedMovieCandidates()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TrackedMovies
            SET SelectedCandidateName = NULL,
                SelectedCandidateUrl = NULL,
                SelectedCandidatePlugin = NULL,
                SelectedCandidateFileSize = 0,
                SelectedCandidateSeeders = 0,
                SelectedCandidateQuality = NULL,
                SelectedCandidateAudioCodec = NULL;
            """;
        command.ExecuteNonQuery();
    }

    public long CreateFetchJob(FetchJob job)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FetchJobs (ShowId, TargetKind, ShowTitle, Status, TotalEpisodes, ProcessedEpisodes, ErrorSummary, CreatedUtc, StartedUtc, FinishedUtc)
            VALUES ($ShowId, $TargetKind, $ShowTitle, $Status, $TotalEpisodes, $ProcessedEpisodes, $ErrorSummary, $CreatedUtc, $StartedUtc, $FinishedUtc);
            SELECT last_insert_rowid();
            """;
        AddFetchJobParameters(command, job);
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    public IReadOnlyList<FetchJob> GetFetchJobs()
    {
        var jobs = new List<FetchJob>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ShowId, TargetKind, ShowTitle, Status, TotalEpisodes, ProcessedEpisodes, ErrorSummary, CreatedUtc, StartedUtc, FinishedUtc
            FROM FetchJobs
            ORDER BY CreatedUtc DESC;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            jobs.Add(ReadFetchJob(reader));
        }

        return jobs;
    }

    public FetchJob? GetFetchJob(long id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ShowId, TargetKind, ShowTitle, Status, TotalEpisodes, ProcessedEpisodes, ErrorSummary, CreatedUtc, StartedUtc, FinishedUtc
            FROM FetchJobs
            WHERE Id = $Id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$Id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadFetchJob(reader) : null;
    }

    public void UpdateFetchJob(FetchJob job)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE FetchJobs
            SET ShowId = $ShowId,
                TargetKind = $TargetKind,
                ShowTitle = $ShowTitle,
                Status = $Status,
                TotalEpisodes = $TotalEpisodes,
                ProcessedEpisodes = $ProcessedEpisodes,
                ErrorSummary = $ErrorSummary,
                CreatedUtc = $CreatedUtc,
                StartedUtc = $StartedUtc,
                FinishedUtc = $FinishedUtc
            WHERE Id = $Id;
            """;
        AddFetchJobParameters(command, job);
        command.Parameters.AddWithValue("$Id", job.Id);
        command.ExecuteNonQuery();
    }

    public void DeleteFetchJob(long id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FetchJobs WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id);
        command.ExecuteNonQuery();
    }

    private static void InitializeSeriesMappings(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SeriesMappings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ParsedTitle TEXT NOT NULL,
                NormalizedParsedTitle TEXT NOT NULL,
                ParserPattern INTEGER NOT NULL,
                MatchedTitle TEXT NOT NULL,
                MatchedYear INTEGER NULL,
                Provider TEXT NOT NULL,
                ProviderId TEXT NOT NULL,
                UseAbsoluteAnimeMapping INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE(NormalizedParsedTitle, ParserPattern)
            );
        """;
        command.ExecuteNonQuery();
    }

    private static void InitializeTrackedShows(SqliteConnection connection)
    {
        using var shows = connection.CreateCommand();
        shows.CommandText = """
            CREATE TABLE IF NOT EXISTS TrackedShows (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TmdbId INTEGER NOT NULL UNIQUE,
                Title TEXT NOT NULL,
                FirstAirYear INTEGER NULL,
                Overview TEXT NULL,
                PosterPath TEXT NULL,
                RecipeId TEXT NULL,
                PreferredQuality TEXT NOT NULL DEFAULT '1080p',
                PreferredAudioCodec TEXT NOT NULL DEFAULT '',
                MinimumSeeders INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """;
        shows.ExecuteNonQuery();
        EnsureColumn(connection, "TrackedShows", "RecipeId", "TEXT NULL");
        EnsureColumn(connection, "TrackedShows", "PackRecipeId", "TEXT NULL");
        EnsureColumn(connection, "TrackedShows", "PreferredAudioCodec", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "TrackedShows", "MinimumSeeders", "INTEGER NOT NULL DEFAULT 0");

        using var seasons = connection.CreateCommand();
        seasons.CommandText = """
            CREATE TABLE IF NOT EXISTS TrackedSeasons (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ShowId INTEGER NOT NULL,
                SeasonNumber INTEGER NOT NULL,
                EpisodeCount INTEGER NOT NULL DEFAULT 0,
                DownloadFolder TEXT NULL,
                ManagementMode INTEGER NOT NULL DEFAULT 0,
                SelectedPackCandidateName TEXT NULL,
                SelectedPackCandidateUrl TEXT NULL,
                SelectedPackCandidatePlugin TEXT NULL,
                SelectedPackCandidateFileSize INTEGER NOT NULL DEFAULT 0,
                SelectedPackCandidateSeeders INTEGER NOT NULL DEFAULT 0,
                SelectedPackCandidateQuality TEXT NULL,
                SelectedPackCandidateAudioCodec TEXT NULL,
                SelectedPackCoveredSeasons TEXT NULL,
                SelectedPackOwnerSeasonNumber INTEGER NULL,
                PackTorrentHash TEXT NULL,
                PackTorrentName TEXT NULL,
                PackTorrentState TEXT NULL,
                PackTorrentProgress REAL NOT NULL DEFAULT 0,
                UNIQUE(ShowId, SeasonNumber),
                FOREIGN KEY(ShowId) REFERENCES TrackedShows(Id) ON DELETE CASCADE
            );
            """;
        seasons.ExecuteNonQuery();
        EnsureColumn(connection, "TrackedSeasons", "DownloadFolder", "TEXT NULL");
        EnsureColumn(connection, "TrackedSeasons", "ManagementMode", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TrackedSeasons", "SelectedPackCandidateName", "TEXT NULL");
        EnsureColumn(connection, "TrackedSeasons", "SelectedPackCandidateUrl", "TEXT NULL");
        EnsureColumn(connection, "TrackedSeasons", "SelectedPackCandidatePlugin", "TEXT NULL");
        EnsureColumn(connection, "TrackedSeasons", "SelectedPackCandidateFileSize", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TrackedSeasons", "SelectedPackCandidateSeeders", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TrackedSeasons", "SelectedPackCandidateQuality", "TEXT NULL");
        EnsureColumn(connection, "TrackedSeasons", "SelectedPackCandidateAudioCodec", "TEXT NULL");
        EnsureColumn(connection, "TrackedSeasons", "SelectedPackCoveredSeasons", "TEXT NULL");
        EnsureColumn(connection, "TrackedSeasons", "SelectedPackOwnerSeasonNumber", "INTEGER NULL");
        EnsureColumn(connection, "TrackedSeasons", "PackTorrentHash", "TEXT NULL");
        EnsureColumn(connection, "TrackedSeasons", "PackTorrentName", "TEXT NULL");
        EnsureColumn(connection, "TrackedSeasons", "PackTorrentState", "TEXT NULL");
        EnsureColumn(connection, "TrackedSeasons", "PackTorrentProgress", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TrackedSeasons", "IsHidden", "INTEGER NOT NULL DEFAULT 0");

        using var episodes = connection.CreateCommand();
        episodes.CommandText = """
            CREATE TABLE IF NOT EXISTS TrackedEpisodes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ShowId INTEGER NOT NULL,
                SeasonNumber INTEGER NOT NULL,
                EpisodeNumber INTEGER NOT NULL,
                Title TEXT NOT NULL,
                AirDate TEXT NULL,
                Availability INTEGER NOT NULL DEFAULT 0,
                IsWanted INTEGER NOT NULL DEFAULT 0,
                TorrentHash TEXT NULL,
                TorrentName TEXT NULL,
                TorrentState TEXT NULL,
                TorrentProgress REAL NOT NULL DEFAULT 0,
                TorrentUpdatedUtc TEXT NULL,
                SelectedCandidateName TEXT NULL,
                SelectedCandidateUrl TEXT NULL,
                SelectedCandidatePlugin TEXT NULL,
                SelectedCandidateFileSize INTEGER NOT NULL DEFAULT 0,
                SelectedCandidateSeeders INTEGER NOT NULL DEFAULT 0,
                SelectedCandidateQuality TEXT NULL,
                SelectedCandidateAudioCodec TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE(ShowId, SeasonNumber, EpisodeNumber),
                FOREIGN KEY(ShowId) REFERENCES TrackedShows(Id) ON DELETE CASCADE
            );
            """;
        episodes.ExecuteNonQuery();
        EnsureColumn(connection, "TrackedEpisodes", "TorrentHash", "TEXT NULL");
        EnsureColumn(connection, "TrackedEpisodes", "TorrentName", "TEXT NULL");
        EnsureColumn(connection, "TrackedEpisodes", "TorrentState", "TEXT NULL");
        EnsureColumn(connection, "TrackedEpisodes", "TorrentProgress", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TrackedEpisodes", "TorrentUpdatedUtc", "TEXT NULL");
        EnsureColumn(connection, "TrackedEpisodes", "SelectedCandidateName", "TEXT NULL");
        EnsureColumn(connection, "TrackedEpisodes", "SelectedCandidateUrl", "TEXT NULL");
        EnsureColumn(connection, "TrackedEpisodes", "SelectedCandidatePlugin", "TEXT NULL");
        EnsureColumn(connection, "TrackedEpisodes", "SelectedCandidateFileSize", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TrackedEpisodes", "SelectedCandidateSeeders", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TrackedEpisodes", "SelectedCandidateQuality", "TEXT NULL");
        EnsureColumn(connection, "TrackedEpisodes", "SelectedCandidateAudioCodec", "TEXT NULL");
    }

    private static void InitializeTrackedMovies(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TrackedMovies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TmdbId INTEGER NOT NULL UNIQUE,
                Title TEXT NOT NULL,
                ReleaseYear INTEGER NULL,
                Overview TEXT NULL,
                PosterPath TEXT NULL,
                RecipeId TEXT NULL,
                PreferredQuality TEXT NOT NULL DEFAULT '1080p',
                PreferredAudioCodec TEXT NOT NULL DEFAULT '',
                MinimumSeeders INTEGER NOT NULL DEFAULT 0,
                Availability INTEGER NOT NULL DEFAULT 0,
                IsWanted INTEGER NOT NULL DEFAULT 1,
                TorrentHash TEXT NULL,
                TorrentName TEXT NULL,
                TorrentState TEXT NULL,
                TorrentProgress REAL NOT NULL DEFAULT 0,
                TorrentUpdatedUtc TEXT NULL,
                SelectedCandidateName TEXT NULL,
                SelectedCandidateUrl TEXT NULL,
                SelectedCandidatePlugin TEXT NULL,
                SelectedCandidateFileSize INTEGER NOT NULL DEFAULT 0,
                SelectedCandidateSeeders INTEGER NOT NULL DEFAULT 0,
                SelectedCandidateQuality TEXT NULL,
                SelectedCandidateAudioCodec TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "TrackedMovies", "RecipeId", "TEXT NULL");
        EnsureColumn(connection, "TrackedMovies", "SelectedCandidateName", "TEXT NULL");
        EnsureColumn(connection, "TrackedMovies", "SelectedCandidateUrl", "TEXT NULL");
        EnsureColumn(connection, "TrackedMovies", "SelectedCandidatePlugin", "TEXT NULL");
        EnsureColumn(connection, "TrackedMovies", "SelectedCandidateFileSize", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TrackedMovies", "SelectedCandidateSeeders", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TrackedMovies", "SelectedCandidateQuality", "TEXT NULL");
        EnsureColumn(connection, "TrackedMovies", "SelectedCandidateAudioCodec", "TEXT NULL");
    }

    private static void InitializeFetchJobs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS FetchJobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ShowId INTEGER NOT NULL,
                TargetKind INTEGER NOT NULL DEFAULT 1,
                ShowTitle TEXT NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                TotalEpisodes INTEGER NOT NULL DEFAULT 0,
                ProcessedEpisodes INTEGER NOT NULL DEFAULT 0,
                ErrorSummary TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                StartedUtc TEXT NULL,
                FinishedUtc TEXT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "FetchJobs", "TargetKind", "INTEGER NOT NULL DEFAULT 1");
    }

    private static void InitializeTorrentCartOrders(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TorrentCartOrders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TargetKind INTEGER NOT NULL,
                MediaId INTEGER NOT NULL,
                EpisodeId INTEGER NULL,
                SeasonNumber INTEGER NULL,
                EpisodeNumber INTEGER NULL,
                Title TEXT NOT NULL,
                Summary TEXT NOT NULL,
                Status INTEGER NOT NULL,
                StatusDetail TEXT NOT NULL DEFAULT '',
                SelectedCandidateName TEXT NOT NULL DEFAULT '',
                SelectedCandidateUrl TEXT NOT NULL DEFAULT '',
                SelectedCandidatePlugin TEXT NOT NULL DEFAULT '',
                SelectedCandidateFileSize INTEGER NOT NULL DEFAULT 0,
                SelectedCandidateSeeders INTEGER NOT NULL DEFAULT 0,
                SelectedCandidateLeechers INTEGER NOT NULL DEFAULT 0,
                SelectedCandidateQuality TEXT NOT NULL DEFAULT '',
                SelectedCandidateAudioCodec TEXT NOT NULL DEFAULT '',
                SelectedCandidateCoveredSeasons TEXT NOT NULL DEFAULT '',
                SelectedCandidateTotalScore INTEGER NOT NULL DEFAULT 0,
                TorrentHash TEXT NOT NULL DEFAULT '',
                TorrentName TEXT NOT NULL DEFAULT '',
                TorrentState TEXT NOT NULL DEFAULT '',
                TorrentProgress REAL NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "TorrentCartOrders", "SelectedCandidateLeechers", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TorrentCartOrders", "SelectedCandidateCoveredSeasons", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "TorrentCartOrders", "SelectedCandidateTotalScore", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TorrentCartOrders", "TorrentHash", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "TorrentCartOrders", "TorrentName", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "TorrentCartOrders", "TorrentState", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "TorrentCartOrders", "TorrentProgress", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TorrentCartOrders", "CreatedUtc", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z'");
        EnsureColumn(connection, "TorrentCartOrders", "UpdatedUtc", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z'");
    }

    private static void InitializeTorrentCartOrderCandidates(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TorrentCartOrderCandidates (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderId INTEGER NOT NULL,
                Rank INTEGER NOT NULL DEFAULT 0,
                IsSelected INTEGER NOT NULL DEFAULT 0,
                IsAccepted INTEGER NOT NULL DEFAULT 0,
                Name TEXT NOT NULL DEFAULT '',
                Url TEXT NOT NULL DEFAULT '',
                PluginName TEXT NOT NULL DEFAULT '',
                FileSize INTEGER NOT NULL DEFAULT 0,
                Seeders INTEGER NOT NULL DEFAULT 0,
                Leechers INTEGER NOT NULL DEFAULT 0,
                Quality TEXT NOT NULL DEFAULT '',
                AudioCodec TEXT NOT NULL DEFAULT '',
                CoveredSeasons TEXT NOT NULL DEFAULT '',
                TotalScore INTEGER NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "TorrentCartOrderCandidates", "IsAccepted", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "TorrentCartOrderCandidates", "CoveredSeasons", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "TorrentCartOrderCandidates", "TotalScore", "INTEGER NOT NULL DEFAULT 0");
    }

    private TrackedShow? GetTrackedShowCore(string whereClause, object value)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT s.Id, s.TmdbId, s.Title, s.FirstAirYear, s.Overview, s.PosterPath, s.RecipeId, s.PackRecipeId, s.PreferredQuality, s.PreferredAudioCodec, s.MinimumSeeders, s.CreatedUtc, s.UpdatedUtc,
                   COUNT(e.Id), SUM(CASE WHEN e.Availability = 1 THEN 1 ELSE 0 END), SUM(CASE WHEN e.IsWanted = 1 THEN 1 ELSE 0 END)
            FROM TrackedShows s
            LEFT JOIN TrackedEpisodes e ON e.ShowId = s.Id
            WHERE {whereClause}
            GROUP BY s.Id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$Value", value);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTrackedShow(reader) : null;
    }

    private static TrackedShow ReadTrackedShow(SqliteDataReader reader)
    {
        return new TrackedShow
        {
            Id = reader.GetInt64(0),
            TmdbId = reader.GetInt32(1),
            Title = reader.GetString(2),
            FirstAirYear = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Overview = reader.IsDBNull(4) ? null : reader.GetString(4),
            PosterPath = reader.IsDBNull(5) ? null : reader.GetString(5),
            RecipeId = reader.IsDBNull(6) ? null : reader.GetString(6),
            PackRecipeId = reader.IsDBNull(7) ? null : reader.GetString(7),
            PreferredQuality = reader.GetString(8),
            PreferredAudioCodec = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
            MinimumSeeders = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
            CreatedUtc = DateTime.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedUtc = DateTime.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind),
            TotalEpisodes = reader.IsDBNull(13) ? 0 : Convert.ToInt32(reader.GetValue(13)),
            AvailableEpisodes = reader.IsDBNull(14) ? 0 : Convert.ToInt32(reader.GetValue(14)),
            WantedEpisodes = reader.IsDBNull(15) ? 0 : Convert.ToInt32(reader.GetValue(15))
        };
    }

    private static TrackedEpisode ReadTrackedEpisode(SqliteDataReader reader)
    {
        var airDateText = reader.IsDBNull(5) ? null : reader.GetString(5);
        return new TrackedEpisode
        {
            Id = reader.GetInt64(0),
            ShowId = reader.GetInt64(1),
            SeasonNumber = reader.GetInt32(2),
            EpisodeNumber = reader.GetInt32(3),
            Title = reader.GetString(4),
            AirDate = DateTime.TryParse(airDateText, out var airDate) ? airDate : null,
            Availability = (EpisodeAvailability)reader.GetInt32(6),
            IsWanted = reader.GetInt32(7) == 1,
            TorrentHash = reader.IsDBNull(8) ? null : reader.GetString(8),
            TorrentName = reader.IsDBNull(9) ? null : reader.GetString(9),
            TorrentState = reader.IsDBNull(10) ? null : reader.GetString(10),
            TorrentProgress = reader.IsDBNull(11) ? 0 : reader.GetDouble(11),
            TorrentUpdatedUtc = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind),
            SelectedCandidateName = reader.IsDBNull(13) ? null : reader.GetString(13),
            SelectedCandidateUrl = reader.IsDBNull(14) ? null : reader.GetString(14),
            SelectedCandidatePlugin = reader.IsDBNull(15) ? null : reader.GetString(15),
            SelectedCandidateFileSize = reader.IsDBNull(16) ? 0 : reader.GetInt64(16),
            SelectedCandidateSeeders = reader.IsDBNull(17) ? 0 : reader.GetInt32(17),
            SelectedCandidateQuality = reader.IsDBNull(18) ? null : reader.GetString(18),
            SelectedCandidateAudioCodec = reader.IsDBNull(19) ? null : reader.GetString(19),
            CreatedUtc = DateTime.Parse(reader.GetString(20), null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedUtc = DateTime.Parse(reader.GetString(21), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    private TrackedMovie? GetTrackedMovieCore(string whereClause, object value)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, TmdbId, Title, ReleaseYear, Overview, PosterPath, RecipeId, PreferredQuality, PreferredAudioCodec,
                   MinimumSeeders, Availability, IsWanted, TorrentHash, TorrentName, TorrentState,
                   TorrentProgress, TorrentUpdatedUtc, SelectedCandidateName, SelectedCandidateUrl,
                   SelectedCandidatePlugin, SelectedCandidateFileSize, SelectedCandidateSeeders,
                   SelectedCandidateQuality, SelectedCandidateAudioCodec, CreatedUtc, UpdatedUtc
            FROM TrackedMovies
            WHERE {whereClause}
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$Value", value);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTrackedMovie(reader) : null;
    }

    private static TrackedMovie ReadTrackedMovie(SqliteDataReader reader)
    {
        return new TrackedMovie
        {
            Id = reader.GetInt64(0),
            TmdbId = reader.GetInt32(1),
            Title = reader.GetString(2),
            ReleaseYear = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Overview = reader.IsDBNull(4) ? null : reader.GetString(4),
            PosterPath = reader.IsDBNull(5) ? null : reader.GetString(5),
            RecipeId = reader.IsDBNull(6) ? null : reader.GetString(6),
            PreferredQuality = reader.GetString(7),
            PreferredAudioCodec = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            MinimumSeeders = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
            Availability = (EpisodeAvailability)reader.GetInt32(10),
            IsWanted = reader.GetInt32(11) == 1,
            TorrentHash = reader.IsDBNull(12) ? null : reader.GetString(12),
            TorrentName = reader.IsDBNull(13) ? null : reader.GetString(13),
            TorrentState = reader.IsDBNull(14) ? null : reader.GetString(14),
            TorrentProgress = reader.IsDBNull(15) ? 0 : reader.GetDouble(15),
            TorrentUpdatedUtc = reader.IsDBNull(16) ? null : DateTime.Parse(reader.GetString(16), null, System.Globalization.DateTimeStyles.RoundtripKind),
            SelectedCandidateName = reader.IsDBNull(17) ? null : reader.GetString(17),
            SelectedCandidateUrl = reader.IsDBNull(18) ? null : reader.GetString(18),
            SelectedCandidatePlugin = reader.IsDBNull(19) ? null : reader.GetString(19),
            SelectedCandidateFileSize = reader.IsDBNull(20) ? 0 : reader.GetInt64(20),
            SelectedCandidateSeeders = reader.IsDBNull(21) ? 0 : reader.GetInt32(21),
            SelectedCandidateQuality = reader.IsDBNull(22) ? null : reader.GetString(22),
            SelectedCandidateAudioCodec = reader.IsDBNull(23) ? null : reader.GetString(23),
            CreatedUtc = DateTime.Parse(reader.GetString(24), null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedUtc = DateTime.Parse(reader.GetString(25), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    private static void AddTrackedMovieParameters(SqliteCommand command, TrackedMovie movie, DateTime now)
    {
        command.Parameters.AddWithValue("$TmdbId", movie.TmdbId);
        command.Parameters.AddWithValue("$Title", movie.Title);
        command.Parameters.AddWithValue("$ReleaseYear", (object?)movie.ReleaseYear ?? DBNull.Value);
        command.Parameters.AddWithValue("$Overview", (object?)movie.Overview ?? DBNull.Value);
        command.Parameters.AddWithValue("$PosterPath", (object?)movie.PosterPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$RecipeId", (object?)movie.RecipeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$PreferredQuality", string.IsNullOrWhiteSpace(movie.PreferredQuality) ? "1080p" : movie.PreferredQuality);
        command.Parameters.AddWithValue("$PreferredAudioCodec", string.IsNullOrWhiteSpace(movie.PreferredAudioCodec) ? string.Empty : movie.PreferredAudioCodec.Trim());
        command.Parameters.AddWithValue("$MinimumSeeders", Math.Max(0, movie.MinimumSeeders));
        command.Parameters.AddWithValue("$Availability", (int)movie.Availability);
        command.Parameters.AddWithValue("$IsWanted", movie.IsWanted ? 1 : 0);
        command.Parameters.AddWithValue("$TorrentHash", (object?)movie.TorrentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$TorrentName", (object?)movie.TorrentName ?? DBNull.Value);
        command.Parameters.AddWithValue("$TorrentState", (object?)movie.TorrentState ?? DBNull.Value);
        command.Parameters.AddWithValue("$TorrentProgress", Math.Clamp(movie.TorrentProgress, 0, 1));
        command.Parameters.AddWithValue("$TorrentUpdatedUtc", (object?)movie.TorrentUpdatedUtc?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$SelectedCandidateName", (object?)movie.SelectedCandidateName ?? DBNull.Value);
        command.Parameters.AddWithValue("$SelectedCandidateUrl", (object?)movie.SelectedCandidateUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$SelectedCandidatePlugin", (object?)movie.SelectedCandidatePlugin ?? DBNull.Value);
        command.Parameters.AddWithValue("$SelectedCandidateFileSize", movie.SelectedCandidateFileSize);
        command.Parameters.AddWithValue("$SelectedCandidateSeeders", movie.SelectedCandidateSeeders);
        command.Parameters.AddWithValue("$SelectedCandidateQuality", (object?)movie.SelectedCandidateQuality ?? DBNull.Value);
        command.Parameters.AddWithValue("$SelectedCandidateAudioCodec", (object?)movie.SelectedCandidateAudioCodec ?? DBNull.Value);
        command.Parameters.AddWithValue("$CreatedUtc", (movie.CreatedUtc == default ? now : movie.CreatedUtc).ToString("O"));
        command.Parameters.AddWithValue("$UpdatedUtc", now.ToString("O"));
    }

    private static void AddSelectedCandidateParameters(SqliteCommand command, EpisodeFetchCandidate candidate)
    {
        command.Parameters.AddWithValue("$Name", candidate.FileName);
        command.Parameters.AddWithValue("$Url", candidate.FileUrl);
        command.Parameters.AddWithValue("$Plugin", candidate.PluginName);
        command.Parameters.AddWithValue("$FileSize", candidate.FileSize);
        command.Parameters.AddWithValue("$Seeders", candidate.Seeders);
        command.Parameters.AddWithValue("$Quality", string.IsNullOrWhiteSpace(candidate.QualityLabel) ? DBNull.Value : candidate.QualityLabel);
        command.Parameters.AddWithValue("$AudioCodec", string.IsNullOrWhiteSpace(candidate.AudioCodecLabel) ? DBNull.Value : candidate.AudioCodecLabel);
    }

    private static void AddFetchJobParameters(SqliteCommand command, FetchJob job)
    {
        command.Parameters.AddWithValue("$ShowId", job.ShowId);
        command.Parameters.AddWithValue("$TargetKind", (int)job.TargetKind);
        command.Parameters.AddWithValue("$ShowTitle", job.ShowTitle);
        command.Parameters.AddWithValue("$Status", (int)job.Status);
        command.Parameters.AddWithValue("$TotalEpisodes", job.TotalEpisodes);
        command.Parameters.AddWithValue("$ProcessedEpisodes", job.ProcessedEpisodes);
        command.Parameters.AddWithValue("$ErrorSummary", (object?)job.ErrorSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("$CreatedUtc", (job.CreatedUtc == default ? DateTime.UtcNow : job.CreatedUtc).ToString("O"));
        command.Parameters.AddWithValue("$StartedUtc", (object?)job.StartedUtc?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$FinishedUtc", (object?)job.FinishedUtc?.ToString("O") ?? DBNull.Value);
    }

    private static void AddTorrentCartOrderParameters(SqliteCommand command, TorrentCartOrder order)
    {
        command.Parameters.AddWithValue("$TargetKind", (int)order.TargetKind);
        command.Parameters.AddWithValue("$MediaId", order.MediaId);
        command.Parameters.AddWithValue("$EpisodeId", (object?)order.EpisodeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$SeasonNumber", (object?)order.SeasonNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$EpisodeNumber", (object?)order.EpisodeNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$Title", order.Title);
        command.Parameters.AddWithValue("$Summary", order.Summary);
        command.Parameters.AddWithValue("$Status", (int)order.Status);
        command.Parameters.AddWithValue("$StatusDetail", order.StatusDetail);
        command.Parameters.AddWithValue("$SelectedCandidateName", order.SelectedCandidateName);
        command.Parameters.AddWithValue("$SelectedCandidateUrl", order.SelectedCandidateUrl);
        command.Parameters.AddWithValue("$SelectedCandidatePlugin", order.SelectedCandidatePlugin);
        command.Parameters.AddWithValue("$SelectedCandidateFileSize", order.SelectedCandidateFileSize);
        command.Parameters.AddWithValue("$SelectedCandidateSeeders", order.SelectedCandidateSeeders);
        command.Parameters.AddWithValue("$SelectedCandidateLeechers", order.SelectedCandidateLeechers);
        command.Parameters.AddWithValue("$SelectedCandidateQuality", order.SelectedCandidateQuality);
        command.Parameters.AddWithValue("$SelectedCandidateAudioCodec", order.SelectedCandidateAudioCodec);
        command.Parameters.AddWithValue("$SelectedCandidateCoveredSeasons", order.SelectedCandidateCoveredSeasons);
        command.Parameters.AddWithValue("$SelectedCandidateTotalScore", order.SelectedCandidateTotalScore);
        command.Parameters.AddWithValue("$TorrentHash", order.TorrentHash);
        command.Parameters.AddWithValue("$TorrentName", order.TorrentName);
        command.Parameters.AddWithValue("$TorrentState", order.TorrentState);
        command.Parameters.AddWithValue("$TorrentProgress", Math.Clamp(order.TorrentProgress, 0, 1));
        command.Parameters.AddWithValue("$CreatedUtc", (order.CreatedUtc == default ? DateTime.UtcNow : order.CreatedUtc).ToString("O"));
        command.Parameters.AddWithValue("$UpdatedUtc", order.UpdatedUtc.ToString("O"));
    }

    private static void AddTorrentCartOrderCandidateParameters(
        SqliteCommand command,
        long orderId,
        TorrentCartOrderCandidate candidate,
        int index)
    {
        command.Parameters.AddWithValue("$OrderId", orderId);
        command.Parameters.AddWithValue("$Rank", candidate.Rank <= 0 ? index + 1 : candidate.Rank);
        command.Parameters.AddWithValue("$IsSelected", candidate.IsSelected ? 1 : 0);
        command.Parameters.AddWithValue("$IsAccepted", candidate.IsAccepted ? 1 : 0);
        command.Parameters.AddWithValue("$Name", candidate.Name);
        command.Parameters.AddWithValue("$Url", candidate.Url);
        command.Parameters.AddWithValue("$PluginName", candidate.PluginName);
        command.Parameters.AddWithValue("$FileSize", candidate.FileSize);
        command.Parameters.AddWithValue("$Seeders", candidate.Seeders);
        command.Parameters.AddWithValue("$Leechers", candidate.Leechers);
        command.Parameters.AddWithValue("$Quality", candidate.Quality);
        command.Parameters.AddWithValue("$AudioCodec", candidate.AudioCodec);
        command.Parameters.AddWithValue("$CoveredSeasons", candidate.CoveredSeasons);
        command.Parameters.AddWithValue("$TotalScore", candidate.TotalScore);
    }

    private static FetchJob ReadFetchJob(SqliteDataReader reader)
    {
        return new FetchJob
        {
            Id = reader.GetInt64(0),
            ShowId = reader.GetInt64(1),
            TargetKind = (MediaKind)reader.GetInt32(2),
            ShowTitle = reader.GetString(3),
            Status = (FetchJobStatus)reader.GetInt32(4),
            TotalEpisodes = reader.GetInt32(5),
            ProcessedEpisodes = reader.GetInt32(6),
            ErrorSummary = reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedUtc = DateTime.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind),
            StartedUtc = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind),
            FinishedUtc = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    private static TorrentCartOrder ReadTorrentCartOrder(SqliteDataReader reader)
    {
        return new TorrentCartOrder
        {
            Id = reader.GetInt64(0),
            TargetKind = (MediaKind)reader.GetInt32(1),
            MediaId = reader.GetInt64(2),
            EpisodeId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
            SeasonNumber = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            EpisodeNumber = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            Title = reader.GetString(6),
            Summary = reader.GetString(7),
            Status = (TorrentOrderStatus)reader.GetInt32(8),
            StatusDetail = reader.GetString(9),
            SelectedCandidateName = reader.GetString(10),
            SelectedCandidateUrl = reader.GetString(11),
            SelectedCandidatePlugin = reader.GetString(12),
            SelectedCandidateFileSize = reader.GetInt64(13),
            SelectedCandidateSeeders = reader.GetInt32(14),
            SelectedCandidateLeechers = reader.GetInt32(15),
            SelectedCandidateQuality = reader.GetString(16),
            SelectedCandidateAudioCodec = reader.GetString(17),
            SelectedCandidateCoveredSeasons = reader.GetString(18),
            SelectedCandidateTotalScore = reader.GetInt32(19),
            TorrentHash = reader.GetString(20),
            TorrentName = reader.GetString(21),
            TorrentState = reader.GetString(22),
            TorrentProgress = reader.GetDouble(23),
            CreatedUtc = DateTime.Parse(reader.GetString(24), null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedUtc = DateTime.Parse(reader.GetString(25), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    private static TorrentCartOrderCandidate ReadTorrentCartOrderCandidate(SqliteDataReader reader)
    {
        return new TorrentCartOrderCandidate
        {
            Id = reader.GetInt64(0),
            OrderId = reader.GetInt64(1),
            Rank = reader.GetInt32(2),
            IsSelected = reader.GetInt32(3) == 1,
            IsAccepted = reader.GetInt32(4) == 1,
            Name = reader.GetString(5),
            Url = reader.GetString(6),
            PluginName = reader.GetString(7),
            FileSize = reader.GetInt64(8),
            Seeders = reader.GetInt32(9),
            Leechers = reader.GetInt32(10),
            Quality = reader.GetString(11),
            AudioCodec = reader.GetString(12),
            CoveredSeasons = reader.GetString(13),
            TotalScore = reader.GetInt32(14)
        };
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeTitleKey(string value)
    {
        return string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

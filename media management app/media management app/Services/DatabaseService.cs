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
                LastSeenUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "SourceItems", "MediaKind", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SourceItems", "ParserPattern", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SourceItems", "MovieTitle", "TEXT NULL");
        EnsureColumn(connection, "SourceItems", "MovieYear", "INTEGER NULL");
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
        InitializeSeriesMappings(connection);
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
                   ShowTitle, MovieTitle, MovieYear, SeasonNumber, EpisodeNumber, EpisodeTitle,
                   MatchedTitle, MatchedYear, Provider, ProviderId, MatchConfidence, MatchReason,
                   RequiresManualReview, MatchAccepted, UseAbsoluteAnimeMapping,
                   State, Notes, LinkedPath, LastSeenUtc
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

        command.CommandText = $"""
            INSERT INTO SourceItems (SourceRootFolder, ParentFolder, FilePath, FileName, ScanText, MediaKind, ParserPattern, ShowTitle, MovieTitle, MovieYear, SeasonNumber, EpisodeNumber, EpisodeTitle, MatchedTitle, MatchedYear, Provider, ProviderId, MatchConfidence, MatchReason, RequiresManualReview, MatchAccepted, UseAbsoluteAnimeMapping, State, Notes, LinkedPath, LastSeenUtc)
            VALUES ($SourceRootFolder, $ParentFolder, $FilePath, $FileName, $ScanText, $MediaKind, $ParserPattern, $ShowTitle, $MovieTitle, $MovieYear, $SeasonNumber, $EpisodeNumber, $EpisodeTitle, $MatchedTitle, $MatchedYear, $Provider, $ProviderId, $MatchConfidence, $MatchReason, $RequiresManualReview, $MatchAccepted, $UseAbsoluteAnimeMapping, $State, $Notes, $LinkedPath, $LastSeenUtc)
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
            EpisodeTitle = reader.IsDBNull(13) ? null : reader.GetString(13),
            MatchedTitle = reader.IsDBNull(14) ? null : reader.GetString(14),
            MatchedYear = reader.IsDBNull(15) ? null : reader.GetInt32(15),
            Provider = reader.IsDBNull(16) ? null : reader.GetString(16),
            ProviderId = reader.IsDBNull(17) ? null : reader.GetString(17),
            MatchConfidence = reader.IsDBNull(18) ? null : reader.GetDouble(18),
            MatchReason = reader.IsDBNull(19) ? null : reader.GetString(19),
            RequiresManualReview = reader.GetInt32(20) == 1,
            MatchAccepted = reader.GetInt32(21) == 1,
            UseAbsoluteAnimeMapping = reader.GetInt32(22) == 1,
            State = (ItemState)reader.GetInt32(23),
            Notes = reader.IsDBNull(24) ? null : reader.GetString(24),
            LinkedPath = reader.IsDBNull(25) ? null : reader.GetString(25),
            LastSeenUtc = DateTime.Parse(reader.GetString(26), null, System.Globalization.DateTimeStyles.RoundtripKind)
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

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeTitleKey(string value)
    {
        return string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

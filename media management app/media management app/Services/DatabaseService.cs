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
                ShowTitle TEXT NULL,
                SeasonNumber INTEGER NULL,
                EpisodeNumber INTEGER NULL,
                EpisodeTitle TEXT NULL,
                State INTEGER NOT NULL,
                Notes TEXT NULL,
                LinkedPath TEXT NULL,
                LastSeenUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "SourceItems", "LinkedPath", "TEXT NULL");
        _logger.Info("SQLite database is ready", LogTarget.File | LogTarget.Ui | LogTarget.Console);
    }

    public IReadOnlyList<SourceItem> GetSourceItems()
    {
        var results = new List<SourceItem>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SourceRootFolder, ParentFolder, FilePath, FileName, ScanText, ShowTitle, SeasonNumber, EpisodeNumber, EpisodeTitle, State, Notes, LinkedPath, LastSeenUtc FROM SourceItems ORDER BY LastSeenUtc DESC;";
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
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SourceItems (SourceRootFolder, ParentFolder, FilePath, FileName, ScanText, ShowTitle, SeasonNumber, EpisodeNumber, EpisodeTitle, State, Notes, LinkedPath, LastSeenUtc)
            VALUES ($SourceRootFolder, $ParentFolder, $FilePath, $FileName, $ScanText, $ShowTitle, $SeasonNumber, $EpisodeNumber, $EpisodeTitle, $State, $Notes, $LinkedPath, $LastSeenUtc)
            ON CONFLICT(FilePath) DO UPDATE SET
                SourceRootFolder = excluded.SourceRootFolder,
                ParentFolder = excluded.ParentFolder,
                FileName = excluded.FileName,
                ScanText = excluded.ScanText,
                ShowTitle = excluded.ShowTitle,
                SeasonNumber = excluded.SeasonNumber,
                EpisodeNumber = excluded.EpisodeNumber,
                EpisodeTitle = excluded.EpisodeTitle,
                State = CASE WHEN SourceItems.State = 3 THEN SourceItems.State ELSE excluded.State END,
                Notes = excluded.Notes,
                LinkedPath = COALESCE(excluded.LinkedPath, SourceItems.LinkedPath),
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
            UpsertSourceItem(item);
            count++;
        }

        _logger.Info($"Persisted {count} source item(s)", LogTarget.File | LogTarget.Console);
    }

    private static void AddParameters(SqliteCommand command, SourceItem item)
    {
        command.Parameters.AddWithValue("$SourceRootFolder", item.SourceRootFolder);
        command.Parameters.AddWithValue("$ParentFolder", item.ParentFolder);
        command.Parameters.AddWithValue("$FilePath", item.FilePath);
        command.Parameters.AddWithValue("$FileName", item.FileName);
        command.Parameters.AddWithValue("$ScanText", item.ScanText);
        command.Parameters.AddWithValue("$ShowTitle", (object?)item.ShowTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$SeasonNumber", (object?)item.SeasonNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$EpisodeNumber", (object?)item.EpisodeNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$EpisodeTitle", (object?)item.EpisodeTitle ?? DBNull.Value);
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
            ShowTitle = reader.IsDBNull(6) ? null : reader.GetString(6),
            SeasonNumber = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            EpisodeNumber = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            EpisodeTitle = reader.IsDBNull(9) ? null : reader.GetString(9),
            State = (ItemState)reader.GetInt32(10),
            Notes = reader.IsDBNull(11) ? null : reader.GetString(11),
            LinkedPath = reader.IsDBNull(12) ? null : reader.GetString(12),
            LastSeenUtc = DateTime.Parse(reader.GetString(13), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }
}

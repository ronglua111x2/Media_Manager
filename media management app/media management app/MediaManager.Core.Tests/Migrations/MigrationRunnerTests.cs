using FluentAssertions;
using media_management_app.Migrations;
using Microsoft.Data.Sqlite;

namespace MediaManager.Core.Tests.Migrations;

public class MigrationRunnerTests
{
    private static readonly string[] ApplicationTables =
    [
        "SourceItems",
        "SeriesMappings",
        "TrackedShows",
        "TrackedSeasons",
        "TrackedEpisodes",
        "TrackedMovies",
        "FetchJobs",
        "TorrentCartOrders",
        "TorrentCartOrderCandidates",
        "TorrentBlacklist"
    ];

    [Fact]
    public void ApplyPending_EmptyDatabase_AppliesAllMigrationsAndCreatesTables()
    {
        using var db = TemporaryDatabase.Create();

        MigrationRunner.ApplyPendingMigrations(db.Connection);

        GetAppliedNames(db.Connection).Should().Equal("001_baseline", "002_fetchjobs_legacy_purge");
        GetUserTableNames(db.Connection).Should().Contain(ApplicationTables);
        GetUserTableNames(db.Connection).Should().Contain(MigrationRunner.HistoryTableName);
        GetAppliedUtcValues(db.Connection).Should().OnlyContain(value => IsRoundtripDateTime(value));
    }

    [Fact]
    public void ApplyPending_RunTwice_IsIdempotent()
    {
        using var db = TemporaryDatabase.Create();

        MigrationRunner.ApplyPendingMigrations(db.Connection);
        var first = GetAppliedRows(db.Connection);

        MigrationRunner.ApplyPendingMigrations(db.Connection);
        var second = GetAppliedRows(db.Connection);

        second.Should().Equal(first);
    }

    [Fact]
    public void ApplyPending_LegacyFetchJobs_ArePurgedOnce()
    {
        using var db = TemporaryDatabase.Create();
        SeedLegacyFetchJobsTable(db.Connection);

        MigrationRunner.ApplyPendingMigrations(db.Connection);
        CountFetchJobs(db.Connection).Should().Be(0);

        InsertFetchJob(db.Connection, showId: 99, title: "ShouldSurviveSecondRun");
        CountFetchJobs(db.Connection).Should().Be(1);

        MigrationRunner.ApplyPendingMigrations(db.Connection);
        CountFetchJobs(db.Connection).Should().Be(1);
        GetAppliedNames(db.Connection).Should().Equal("001_baseline", "002_fetchjobs_legacy_purge");
    }

    private static bool IsRoundtripDateTime(string value)
    {
        return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out _);
    }

    private static List<string> GetAppliedNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM SchemaMigrations ORDER BY Id;";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static List<(string Name, string AppliedUtc)> GetAppliedRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, AppliedUtc FROM SchemaMigrations ORDER BY Id;";
        using var reader = command.ExecuteReader();
        var rows = new List<(string Name, string AppliedUtc)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    private static List<string> GetAppliedUtcValues(SqliteConnection connection)
    {
        return GetAppliedRows(connection).Select(row => row.AppliedUtc).ToList();
    }

    private static List<string> GetUserTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static int CountFetchJobs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FetchJobs;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void SeedLegacyFetchJobsTable(SqliteConnection connection)
    {
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE FetchJobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ShowId INTEGER NOT NULL,
                ShowTitle TEXT NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                TotalEpisodes INTEGER NOT NULL DEFAULT 0,
                ProcessedEpisodes INTEGER NOT NULL DEFAULT 0,
                ErrorSummary TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                StartedUtc TEXT NULL,
                FinishedUtc TEXT NULL,
                TargetKind INTEGER NOT NULL DEFAULT 1
            );
            """;
        create.ExecuteNonQuery();
        InsertFetchJob(connection, showId: 1, title: "Legacy Job A");
        InsertFetchJob(connection, showId: 2, title: "Legacy Job B");
        CountFetchJobs(connection).Should().Be(2);
    }

    private static void InsertFetchJob(SqliteConnection connection, int showId, string title)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO FetchJobs (ShowId, ShowTitle, Status, CreatedUtc)
            VALUES ($ShowId, $ShowTitle, 0, $CreatedUtc);
            """;
        insert.Parameters.AddWithValue("$ShowId", showId);
        insert.Parameters.AddWithValue("$ShowTitle", title);
        insert.Parameters.AddWithValue("$CreatedUtc", DateTime.UtcNow.ToString("o"));
        insert.ExecuteNonQuery();
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _path;

        private TemporaryDatabase(string path, SqliteConnection connection)
        {
            _path = path;
            Connection = connection;
        }

        public SqliteConnection Connection { get; }

        public static TemporaryDatabase Create()
        {
            var path = Path.Combine(Path.GetTempPath(), $"mm-mig-{Guid.NewGuid():N}.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString();
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return new TemporaryDatabase(path, connection);
        }

        public void Dispose()
        {
            Connection.Dispose();
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
            }
        }
    }
}

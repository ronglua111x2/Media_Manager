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

        GetAppliedNames(db.Connection).Should().Equal(
            "001_baseline",
            "002_fetchjobs_legacy_purge",
            "003_torrentblacklist_rebuild",
            "004_episode_rating_thought",
            "005_cart_recipe_max_candidate_overrides",
            "006_cart_recipe_override_sets",
            "007_auto_track_episode_overrides");
        GetUserTableNames(db.Connection).Should().Contain(ApplicationTables);
        HasColumn(db.Connection, "TrackedEpisodes", "UserRating").Should().BeTrue();
        HasColumn(db.Connection, "TrackedEpisodes", "Thought").Should().BeTrue();
        HasColumn(db.Connection, "TrackedShows", "CartEpisodeMaxCandidatesOverride").Should().BeTrue();
        HasColumn(db.Connection, "TrackedShows", "CartPackMaxCandidatesOverride").Should().BeTrue();
        HasColumn(db.Connection, "TrackedMovies", "CartMaxCandidatesOverride").Should().BeTrue();
        HasColumn(db.Connection, "TrackedShows", "CartEpisodeOverridesJson").Should().BeTrue();
        HasColumn(db.Connection, "TrackedShows", "CartPackOverridesJson").Should().BeTrue();
        HasColumn(db.Connection, "TrackedMovies", "CartOverridesJson").Should().BeTrue();
        HasColumn(db.Connection, "TrackedShows", "AutoTrackEpisodeOverridesJson").Should().BeTrue();
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
        GetAppliedNames(db.Connection).Should().Equal(
            "001_baseline",
            "002_fetchjobs_legacy_purge",
            "003_torrentblacklist_rebuild",
            "004_episode_rating_thought",
            "005_cart_recipe_max_candidate_overrides",
            "006_cart_recipe_override_sets",
            "007_auto_track_episode_overrides");
    }

    [Fact]
    public void ApplyPending_LegacyTorrentHashColumn_RebuildsWithoutDataLoss()
    {
        using var db = TemporaryDatabase.Create();
        SeedLegacyTorrentBlacklistWithTorrentHash(db.Connection);

        MigrationRunner.ApplyPendingMigrations(db.Connection);

        GetAppliedNames(db.Connection).Should().Contain("003_torrentblacklist_rebuild");
        HasColumn(db.Connection, "TorrentBlacklist", "TorrentHash").Should().BeFalse();
        HasColumn(db.Connection, "TorrentBlacklist", "InfoHash").Should().BeTrue();

        var rows = ReadBlacklistRows(db.Connection);
        rows.Should().HaveCount(2);
        rows.Should().Contain(row =>
            row.ShowId == 1 &&
            row.InfoHash == "abcdef0123456789abcdef0123456789abcdef01" &&
            row.Reason == "malware");
        rows.Should().Contain(row =>
            row.ShowId == 2 &&
            row.InfoHash == "1111222233334444555566667777888899990000" &&
            row.Reason == "manual");
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

    private static void SeedLegacyTorrentBlacklistWithTorrentHash(SqliteConnection connection)
    {
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE TorrentBlacklist (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ListingUrl TEXT NOT NULL DEFAULT '',
                InfoHash TEXT NOT NULL DEFAULT '',
                TorrentHash TEXT NOT NULL,
                ShowId INTEGER NOT NULL,
                Reason TEXT NOT NULL,
                SuspiciousFilesJson TEXT NULL,
                DateAddedUtc TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Notes TEXT NULL
            );
            """;
        create.ExecuteNonQuery();

        InsertLegacyBlacklistRow(
            connection,
            showId: 1,
            infoHash: "",
            torrentHash: "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
            reason: "malware");
        InsertLegacyBlacklistRow(
            connection,
            showId: 2,
            infoHash: "1111222233334444555566667777888899990000",
            torrentHash: "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef",
            reason: "manual");
    }

    private static void InsertLegacyBlacklistRow(
        SqliteConnection connection,
        int showId,
        string infoHash,
        string torrentHash,
        string reason)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO TorrentBlacklist (ListingUrl, InfoHash, TorrentHash, ShowId, Reason, DateAddedUtc, IsActive)
            VALUES ($ListingUrl, $InfoHash, $TorrentHash, $ShowId, $Reason, $DateAddedUtc, 1);
            """;
        insert.Parameters.AddWithValue("$ListingUrl", $"https://example.test/{showId}");
        insert.Parameters.AddWithValue("$InfoHash", infoHash);
        insert.Parameters.AddWithValue("$TorrentHash", torrentHash);
        insert.Parameters.AddWithValue("$ShowId", showId);
        insert.Parameters.AddWithValue("$Reason", reason);
        insert.Parameters.AddWithValue("$DateAddedUtc", DateTime.UtcNow.ToString("o"));
        insert.ExecuteNonQuery();
    }

    private static bool HasColumn(SqliteConnection connection, string tableName, string columnName)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<(long ShowId, string InfoHash, string Reason)> ReadBlacklistRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ShowId, InfoHash, Reason FROM TorrentBlacklist ORDER BY ShowId;";
        using var reader = command.ExecuteReader();
        var rows = new List<(long ShowId, string InfoHash, string Reason)>();
        while (reader.Read())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
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

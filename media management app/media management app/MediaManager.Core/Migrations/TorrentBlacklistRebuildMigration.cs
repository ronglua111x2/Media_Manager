using Microsoft.Data.Sqlite;

namespace media_management_app.Migrations;

/// <summary>
/// Rebuilds TorrentBlacklist when the legacy TorrentHash column is present.
/// Copies hash data into InfoHash and drops TorrentHash so inserts without that column succeed.
/// </summary>
public static class TorrentBlacklistRebuildMigration
{
    public const string Name = "003_torrentblacklist_rebuild";

    public static void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!HasColumn(connection, transaction, "TorrentBlacklist", "TorrentHash"))
        {
            return;
        }

        using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = """
                CREATE TABLE TorrentBlacklist_v2 (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ListingUrl TEXT NOT NULL DEFAULT '',
                    InfoHash TEXT NOT NULL DEFAULT '',
                    ShowId INTEGER NOT NULL,
                    Reason TEXT NOT NULL,
                    SuspiciousFilesJson TEXT NULL,
                    DateAddedUtc TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    Notes TEXT NULL
                );
                """;
            create.ExecuteNonQuery();
        }

        using (var copy = connection.CreateCommand())
        {
            copy.Transaction = transaction;
            copy.CommandText = """
                INSERT INTO TorrentBlacklist_v2 (
                    Id, ListingUrl, InfoHash, ShowId, Reason, SuspiciousFilesJson, DateAddedUtc, IsActive, Notes)
                SELECT
                    Id,
                    COALESCE(ListingUrl, ''),
                    LOWER(COALESCE(
                        NULLIF(TRIM(InfoHash), ''),
                        NULLIF(TRIM(TorrentHash), ''),
                        '')),
                    ShowId,
                    COALESCE(Reason, ''),
                    SuspiciousFilesJson,
                    COALESCE(DateAddedUtc, ''),
                    COALESCE(IsActive, 1),
                    Notes
                FROM TorrentBlacklist;
                """;
            copy.ExecuteNonQuery();
        }

        using (var drop = connection.CreateCommand())
        {
            drop.Transaction = transaction;
            drop.CommandText = "DROP TABLE TorrentBlacklist;";
            drop.ExecuteNonQuery();
        }

        using (var rename = connection.CreateCommand())
        {
            rename.Transaction = transaction;
            rename.CommandText = "ALTER TABLE TorrentBlacklist_v2 RENAME TO TorrentBlacklist;";
            rename.ExecuteNonQuery();
        }
    }

    private static bool HasColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
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
}

using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace media_management_app.Migrations;

public static class MigrationRunner
{
    public const string HistoryTableName = "SchemaMigrations";

    public static void ApplyPendingMigrations(SqliteConnection connection, Action<string>? logInfo = null)
    {
        try
        {
            EnsureHistoryTable(connection);
            var applied = GetAppliedNames(connection);
            foreach (var migration in LoadAllMigrations())
            {
                if (applied.Contains(migration.Name))
                {
                    continue;
                }

                logInfo?.Invoke($"Applying database migration {migration.Name}");
                using var transaction = connection.BeginTransaction();
                try
                {
                    if (migration.ApplyCode is not null)
                    {
                        migration.ApplyCode(connection, transaction);
                    }
                    else if (!string.IsNullOrWhiteSpace(migration.Sql))
                    {
                        ExecuteSql(connection, transaction, migration.Sql);
                    }

                    RecordApplied(connection, transaction, migration.Name);
                    transaction.Commit();
                    logInfo?.Invoke($"Applied database migration {migration.Name}");
                }
                catch (Exception ex)
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch
                    {
                    }

                    throw new DatabaseMigrationException(migration.Name, ex);
                }
            }
        }
        catch (DatabaseMigrationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DatabaseMigrationException(HistoryTableName, ex);
        }
    }

    private static void EnsureHistoryTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                AppliedUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static HashSet<string> GetAppliedNames(SqliteConnection connection)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM SchemaMigrations;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static void RecordApplied(SqliteConnection connection, SqliteTransaction transaction, string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SchemaMigrations (Name, AppliedUtc)
            VALUES ($Name, $AppliedUtc);
            """;
        command.Parameters.AddWithValue("$Name", name);
        command.Parameters.AddWithValue("$AppliedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void ExecuteSql(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        foreach (var statement in SplitSqlStatements(sql))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    private static IEnumerable<string> SplitSqlStatements(string sql)
    {
        var builder = new StringBuilder();
        using var reader = new StringReader(sql);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            builder.AppendLine(line);
        }

        foreach (var part in builder.ToString().Split(';'))
        {
            var statement = part.Trim();
            if (statement.Length > 0)
            {
                yield return statement;
            }
        }
    }

    private static IReadOnlyList<EmbeddedMigration> LoadAllMigrations()
    {
        var migrations = new Dictionary<string, EmbeddedMigration>(StringComparer.Ordinal);

        foreach (var sqlMigration in LoadEmbeddedSqlMigrations())
        {
            migrations[sqlMigration.Name] = sqlMigration;
        }

        // Conditional rebuild cannot be expressed in plain SQL (needs PRAGMA table_info).
        // Prefer the C# apply path over the no-op marker SQL for the same name.
        migrations[TorrentBlacklistRebuildMigration.Name] = new EmbeddedMigration(
            TorrentBlacklistRebuildMigration.Name,
            Sql: null,
            ApplyCode: TorrentBlacklistRebuildMigration.Apply);

        return migrations.Values
            .OrderBy(migration => migration.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<EmbeddedMigration> LoadEmbeddedSqlMigrations()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var migrations = new List<EmbeddedMigration>();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                || resourceName.IndexOf(".Migrations.", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            const string folderMarker = ".Migrations.";
            var markerIndex = resourceName.IndexOf(folderMarker, StringComparison.OrdinalIgnoreCase);
            var fileName = markerIndex >= 0
                ? resourceName[(markerIndex + folderMarker.Length)..]
                : resourceName;
            var name = Path.GetFileNameWithoutExtension(fileName);
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing embedded migration resource '{resourceName}'.");
            using var reader = new StreamReader(stream);
            migrations.Add(new EmbeddedMigration(name, reader.ReadToEnd(), ApplyCode: null));
        }

        return migrations;
    }

    private sealed record EmbeddedMigration(
        string Name,
        string? Sql,
        Action<SqliteConnection, SqliteTransaction>? ApplyCode);
}

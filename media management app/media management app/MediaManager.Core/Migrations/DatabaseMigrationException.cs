namespace media_management_app.Migrations;

public sealed class DatabaseMigrationException : Exception
{
    public DatabaseMigrationException(string migrationName, Exception innerException)
        : base($"Database migration '{migrationName}' failed: {innerException.Message}", innerException)
    {
        MigrationName = migrationName;
    }

    public string MigrationName { get; }
}

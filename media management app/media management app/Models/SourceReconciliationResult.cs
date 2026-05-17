namespace media_management_app.Models;

public sealed class SourceReconciliationResult
{
    public int MarkedDeletedCount { get; set; }

    public int PurgedStaleCount { get; set; }

    public int CleanupFailureCount { get; set; }
}

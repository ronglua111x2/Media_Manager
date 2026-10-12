namespace media_management_app.Services;

public sealed class QueryTemplateDraft
{
    public QueryTemplateDraft(string pattern, bool committed)
    {
        var trimmed = string.IsNullOrWhiteSpace(pattern) ? string.Empty : pattern.Trim();
        Pattern = trimmed;
        BaselinePattern = trimmed;
        CommittedPattern = committed ? trimmed : null;
    }

    public string Pattern { get; set; }

    public string? CommittedPattern { get; private set; }

    public string BaselinePattern { get; }

    public bool IsCommitted => CommittedPattern is not null;

    public bool IsDirty =>
        !IsCommitted ||
        !string.Equals(Pattern.Trim(), CommittedPattern, StringComparison.Ordinal);

    public void Save()
    {
        CommittedPattern = Pattern.Trim();
    }

    public void Reset()
    {
        Pattern = IsCommitted ? CommittedPattern ?? string.Empty : BaselinePattern;
    }
}

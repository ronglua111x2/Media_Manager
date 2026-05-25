namespace media_management_app.Models;

public sealed class MetadataValidationResult
{
    public bool IsAvailable { get; set; }

    public bool IsValid { get; set; }

    public double Confidence { get; set; }

    public string? Reason { get; set; }

    public string? ErrorMessage { get; set; }
}

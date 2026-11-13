namespace media_management_app.Services;

/// <summary>
/// A search found nothing because no native search source could be asked (every site paused, breaker open, daily
/// budget used). Callers show <see cref="Exception.Message"/> ("No search source available: …") as a failed order
/// instead of "No candidates".
/// </summary>
public sealed class SearchSourceUnavailableException(string message) : Exception(message);

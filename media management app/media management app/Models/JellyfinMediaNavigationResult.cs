namespace media_management_app.Models;

public sealed class JellyfinMediaNavigationResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public static JellyfinMediaNavigationResult Ok() => new() { Succeeded = true };

    public static JellyfinMediaNavigationResult Fail(string message) => new()
    {
        Succeeded = false,
        ErrorMessage = message
    };
}

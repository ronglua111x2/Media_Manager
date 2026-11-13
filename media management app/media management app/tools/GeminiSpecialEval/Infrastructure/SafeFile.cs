namespace GeminiSpecialEval.Infrastructure;

/// <summary>
/// Every file the harness writes goes through here: the API key is scrubbed from the text, and nothing is ever
/// written inside a git work tree (recordings and reports belong in the data folder, outside the public repo).
/// </summary>
internal static class SafeFile
{
    private static string? _secret;

    /// <summary>Registers the API key so that no file or console line can carry it.</summary>
    public static void RegisterSecret(string secret) => _secret = string.IsNullOrEmpty(secret) ? null : secret;

    public static string Scrub(string text) =>
        _secret is null || string.IsNullOrEmpty(text) ? text : text.Replace(_secret, "[REDACTED]", StringComparison.Ordinal);

    public static void WriteAllText(string path, string text)
    {
        var full = Path.GetFullPath(path);
        if (FindGitWorkTree(Path.GetDirectoryName(full)!) is { } repo)
        {
            throw new InvalidOperationException($"Refusing to write '{full}': it is inside the git work tree '{repo}'.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".tmp";
        File.WriteAllText(temp, Scrub(text));
        File.Move(temp, full, overwrite: true);
    }

    /// <summary>The nearest folder at or above <paramref name="folder"/> that holds a <c>.git</c> entry, if any.</summary>
    public static string? FindGitWorkTree(string folder)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(folder)); current is not null; current = current.Parent)
        {
            var git = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return current.FullName;
            }
        }

        return null;
    }
}

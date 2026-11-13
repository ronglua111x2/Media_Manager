using System.Text;
using System.Text.Json;

namespace media_management_app.Search;

/// <summary>
/// &lt;StateFolder&gt;/indexer-state.json, the request governor's memory across restarts (audit 07d GOV-06/07), shared by
/// the app and SearchBench:
/// <list type="bullet">
/// <item>written to a temporary file with write-through and a disk flush, then swapped in with the previous version
/// kept as <c>.bak</c> (a power cut can no longer leave an empty file behind a completed rename);</item>
/// <item>read from the <c>.bak</c> when the main file is unreadable; when both are unreadable the caller starts
/// cautiously (<see cref="IndexerRequestGovernor.StartCautiously"/>) instead of with every limit reset;</item>
/// <item>an exclusive lock file (<c>indexer-state.lock</c>) says which process owns the state: the app holds it while
/// it runs, so SearchBench refuses to run beside it and the two never spend the same IP's budget twice.</item>
/// </list>
/// </summary>
public static class IndexerStateFile
{
    public const string FileName = "indexer-state.json";

    public const string LockFileName = "indexer-state.lock";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <param name="Cautious">Both the file and its backup exist but neither could be read.</param>
    /// <param name="Warning">What went wrong, for the log; null when the main file was read.</param>
    public sealed record LoadResult(IReadOnlyList<IndexerStateSnapshot> States, bool Cautious, string? Warning);

    public static LoadResult Load(string path)
    {
        var backup = path + ".bak";
        if (!File.Exists(path) && !File.Exists(backup))
        {
            return new LoadResult([], false, null);
        }

        if (TryRead(path, out var states, out var error))
        {
            return new LoadResult(states, false, null);
        }

        if (TryRead(backup, out states, out var backupError))
        {
            return new LoadResult(states, false, $"{FileName} could not be read ({error}); restored the previous copy ({FileName}.bak).");
        }

        return new LoadResult(
            [],
            true,
            $"{FileName} could not be read ({error}) and neither could its backup ({backupError}): starting cautiously, " +
            "with today's background budget of every site counted as used (searches started by the user still run).");
    }

    public static void Save(string path, IReadOnlyList<IndexerStateSnapshot> states)
    {
        var json = JsonSerializer.Serialize(states, Json);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = new UTF8Encoding(false).GetBytes(json);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (!File.Exists(path))
        {
            File.Move(temp, path);
            return;
        }

        try
        {
            File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // The backup could not be made (locked by a virus scanner, other volume...): keep the atomic swap at least.
            File.Move(temp, path, overwrite: true);
        }
    }

    /// <summary>
    /// Takes the state folder's exclusive lock; null when another process (the app, or a SearchBench run) holds it.
    /// Dispose to release. The lock file is deleted when the handle closes, also after a crash.
    /// </summary>
    public static IDisposable? TryLock(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return new FileStream(Path.Combine(folder, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryRead(string path, out List<IndexerStateSnapshot> states, out string? error)
    {
        states = [];
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                error = "missing";
                return false;
            }

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart('\0').Length == 0)
            {
                error = "empty";
                return false;
            }

            var read = JsonSerializer.Deserialize<List<IndexerStateSnapshot>>(text);
            if (read is null)
            {
                error = "null";
                return false;
            }

            states = read.Where(state => !string.IsNullOrWhiteSpace(state.IndexerId)).ToList();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }
}

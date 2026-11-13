using GeminiSpecialEval.Infrastructure;

namespace GeminiSpecialEval;

internal sealed class Options
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "include-unconfirmed", "include-stale", "verbose", "no-retry"
    };

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; private init; } = "help";

    public static Options Parse(string[] args)
    {
        var hasCommand = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal);
        var options = new Options { Command = hasCommand ? args[0].ToLowerInvariant() : "help" };
        for (var index = hasCommand ? 1 : 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{arg}'.");
            }

            var name = arg[2..];
            if (Flags.Contains(name))
            {
                options._flags.Add(name);
                continue;
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"--{name} needs a value.");
            }

            options._values[name] = args[++index];
        }

        return options;
    }

    public string? Get(string name) => _values.GetValueOrDefault(name);

    public bool Has(string name) => _flags.Contains(name);

    public int? GetInt(string name) => Get(name) is { } value ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : null;

    public IReadOnlyList<string> GetList(string name, params string[] fallback) =>
        Get(name) is { } value
            ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : fallback;

    /// <summary>The data folder (recordings, ledger, reports): --data-dir or GEMINI_EVAL_DIR, never inside a git work tree.</summary>
    public string? DataDir(bool required)
    {
        var dir = Get("data-dir") ?? Environment.GetEnvironmentVariable("GEMINI_EVAL_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return required
                ? throw new ArgumentException("Pass --data-dir <folder> (or set GEMINI_EVAL_DIR): recordings and reports are kept outside the repo.")
                : null;
        }

        var full = Path.GetFullPath(dir);
        if (SafeFile.FindGitWorkTree(Directory.Exists(full) ? full : Path.GetDirectoryName(full)!) is { } repo)
        {
            throw new ArgumentException($"The data folder must be outside any git work tree (it is inside '{repo}').");
        }

        Directory.CreateDirectory(full);
        return full;
    }

    public const string Usage =
        """
        Gemini special/OVA mapping eval (see README.md).

        Commands (only 'run' sends requests):
          list      [--cases …] [--data-dir …]          cases, rule tier, files Gemini would get, payload sizes
          plan      --models … [--prompts …] [--cases …] [--samples N] --data-dir …   requests a run would send vs today's caps
          run       --models … [--prompts …] [--cases …] [--samples N] --data-dir … [--key-from-settings <settings.json>]
                    [--max-requests N] [--resume <runId>] [--label text] [--no-retry] [--verbose] --yes
                    [--offline-responder oracle|shifted|silent|flaky]   rehearsal: fake answers, nothing sent
          replay    --run <runId>[,<runId>…] --data-dir … [--include-stale]   re-score recordings with zero requests
          selftest  [--data-dir …]                       fake answers (oracle, shifted, silent) through the real pipeline
          quota     --data-dir …                         today's eval requests per model and what is left

        Options:
          --models     catalog ids, e.g. gemini-3.5-flash-lite,gemini-3.1-flash-lite; add @minimal|@low|@medium|@high
                       to replace the catalog's thinking level (gemini-3.5-flash-lite@medium)
          --prompts    prompt variants, e.g. v2,v3 (default: the app's live version)
          --cases      all | name,… | tag:<tag> | source:<fixtures|eval|private>  (default: all)
          --key-from-settings  read Gemini.ApiKey (read-only) from an app settings.json when no
                       GEMINI_EVAL_API_KEY / GEMINI_API_KEY environment variable is set
        Caps (hard): 100 requests per Pacific day over all 500/day models together, 5 per day per 20/day model.
        """;
}

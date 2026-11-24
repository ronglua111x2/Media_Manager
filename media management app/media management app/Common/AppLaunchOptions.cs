namespace media_management_app.Common;

/// <summary>
/// Process launch flags. Parsed once in <c>App.OnStartup</c> and registered in DI.
/// See docs/first-run/01-phase-test-harness.md.
/// </summary>
public sealed class AppLaunchOptions
{
    public const string StateFolderFlag = "--state-folder";
    public const string ForceFirstRunFlag = "--force-first-run";
    public const string EnableBackgroundFlag = "--enable-background";
    public const string UiSnapshotsFlag = "--ui-snapshots";

    public string? StateFolder { get; init; }

    public bool ForceFirstRun { get; init; }

    public bool EnableBackground { get; init; }

    /// <summary>
    /// Dev-only: render every main screen to PNG files in this folder, then exit.
    /// Requires <see cref="StateFolderFlag"/> (a sandbox copy) and refuses <see cref="EnableBackgroundFlag"/>.
    /// See docs/UI_SNAPSHOTS.md.
    /// </summary>
    public string? UiSnapshotOutputFolder { get; init; }

    public bool IsUiSnapshotMode => !string.IsNullOrWhiteSpace(UiSnapshotOutputFolder);

    public bool HasStateFolderOverride => !string.IsNullOrWhiteSpace(StateFolder);

    /// <summary>
    /// True when a test state folder is in use and background jobs must stay off.
    /// </summary>
    public bool SafeTestMode => HasStateFolderOverride && !EnableBackground;

    public static AppLaunchOptions Production { get; } = new();

    public static bool TryParse(IReadOnlyList<string> args, out AppLaunchOptions options, out string? error)
    {
        options = Production;
        error = null;
        string? stateFolder = null;
        var forceFirstRun = false;
        var enableBackground = false;
        string? uiSnapshotOutputFolder = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (TryReadValueFlag(args, ref i, StateFolderFlag, out var folderValue, out var folderError))
            {
                if (folderError is not null)
                {
                    error = folderError;
                    return false;
                }

                stateFolder = folderValue;
                continue;
            }

            if (TryReadValueFlag(args, ref i, UiSnapshotsFlag, out var snapshotValue, out var snapshotError))
            {
                if (snapshotError is not null)
                {
                    error = snapshotError;
                    return false;
                }

                uiSnapshotOutputFolder = snapshotValue;
                continue;
            }

            if (string.Equals(arg, ForceFirstRunFlag, StringComparison.OrdinalIgnoreCase))
            {
                forceFirstRun = true;
                continue;
            }

            if (string.Equals(arg, EnableBackgroundFlag, StringComparison.OrdinalIgnoreCase))
            {
                enableBackground = true;
                continue;
            }
        }

        if (!string.IsNullOrWhiteSpace(stateFolder))
        {
            try
            {
                stateFolder = Path.GetFullPath(stateFolder.Trim());
            }
            catch (Exception ex)
            {
                error = $"Invalid {StateFolderFlag} path: {ex.Message}";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(uiSnapshotOutputFolder))
        {
            if (string.IsNullOrWhiteSpace(stateFolder))
            {
                error = $"{UiSnapshotsFlag} needs {StateFolderFlag} <sandbox copy>; it never runs against the live state folder.";
                return false;
            }

            if (enableBackground)
            {
                error = $"{UiSnapshotsFlag} cannot be combined with {EnableBackgroundFlag}.";
                return false;
            }

            try
            {
                uiSnapshotOutputFolder = Path.GetFullPath(uiSnapshotOutputFolder.Trim());
            }
            catch (Exception ex)
            {
                error = $"Invalid {UiSnapshotsFlag} path: {ex.Message}";
                return false;
            }
        }

        options = new AppLaunchOptions
        {
            StateFolder = stateFolder,
            ForceFirstRun = forceFirstRun,
            EnableBackground = enableBackground,
            UiSnapshotOutputFolder = uiSnapshotOutputFolder
        };
        return true;
    }

    public IReadOnlyList<string> ToRestartArguments()
    {
        var args = new List<string>();
        if (HasStateFolderOverride)
        {
            args.Add(StateFolderFlag);
            args.Add(StateFolder!);
        }

        if (EnableBackground)
        {
            args.Add(EnableBackgroundFlag);
        }

        return args;
    }

    private static bool TryReadValueFlag(
        IReadOnlyList<string> args,
        ref int index,
        string flag,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        var arg = args[index];
        if (arg.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg[(flag.Length + 1)..];
            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"{flag} requires a folder path.";
            }

            return true;
        }

        if (!string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            error = $"{flag} requires a folder path.";
            return true;
        }

        index++;
        value = args[index];
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{flag} requires a folder path.";
        }

        return true;
    }
}

using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.Search;

/// <summary>
/// The backend a search runs on: <paramref name="Requested"/> is the recipe's choice, <paramref name="Effective"/> what
/// actually runs. <paramref name="FallbackReason"/> is set when a Native/Both recipe had to search with qBittorrent
/// because native search is turned off or paused (for the log and the order status).
/// </summary>
public sealed record SearchBackendDecision(string Requested, string Effective, string? FallbackReason);

/// <summary>
/// The recipe decides the search backend (native-search P6 step 1). Settings only turns native search on or off
/// (<see cref="NativeSearchSettings.NativeEnabled"/>) and holds the kill switch. The legacy
/// <see cref="NativeSearchSettings.Backend"/> and <see cref="NativeSearchSettings.BackendByKind"/> are read once by
/// <see cref="SearchBackendMigration"/> and never decide a search.
/// </summary>
public static class SearchBackendResolver
{
    /// <summary>"Native", "Both" or "Qbittorrent" (anything else, "Default" and missing included).</summary>
    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), SearchBackends.Native, StringComparison.OrdinalIgnoreCase) ? SearchBackends.Native
        : string.Equals(value?.Trim(), SearchBackends.Both, StringComparison.OrdinalIgnoreCase) ? SearchBackends.Both
        : SearchBackends.Qbittorrent;

    /// <summary>The recipe's own choice; a recipe without one (new, imported from an older version) uses qBittorrent.</summary>
    public static string RecipeBackend(SearchRecipe? recipe) =>
        recipe is null ? SearchBackends.Qbittorrent : Normalize(RecipeRuntimeSettings.GetSearchBackend(recipe));

    public static bool UsesNative(string backend) => backend is SearchBackends.Native or SearchBackends.Both;

    public static SearchBackendDecision Resolve(SearchRecipe? recipe, bool nativeEnabled, bool pauseAll)
    {
        var requested = RecipeBackend(recipe);
        if (!UsesNative(requested) || (nativeEnabled && !pauseAll))
        {
            return new SearchBackendDecision(requested, requested, null);
        }

        // A hunt must not stall because native search is off: search the way the app did before native existed,
        // and say so in the log and on the order.
        var why = !nativeEnabled
            ? "native search is turned off (Settings > Search engines > Enable native search)"
            : "native search is paused (Settings > Search engines > Pause all native searches)";
        return new SearchBackendDecision(
            requested,
            SearchBackends.Qbittorrent,
            $"Recipe '{recipe?.Name}' is set to {requested}, but {why}; searched with qBittorrent instead.");
    }
}

/// <summary>One recipe whose "Default" backend was replaced by the backend it was effectively using.</summary>
public sealed record RecipeBackendChange(string RecipeId, string RecipeName, string TargetKind, string NewBackend);

public sealed record SearchBackendMigrationResult(bool Ran, IReadOnlyList<RecipeBackendChange> Changes, bool EnabledNative)
{
    /// <summary>One log line per changed recipe, plus one when native search was turned on.</summary>
    public IReadOnlyList<string> Describe()
    {
        var lines = Changes
            .Select(change => $"[Search] recipe '{change.RecipeName}' ({change.TargetKind}, {change.RecipeId}): backend Default -> {change.NewBackend}.")
            .ToList();
        if (EnabledNative)
        {
            lines.Add("[Search] native search enabled: the old Settings backend or a recipe used Native/Both.");
        }

        return lines;
    }
}

/// <summary>
/// One-time move of the backend choice from Settings to the recipes, so nothing changes silently:
/// <list type="bullet">
/// <item>a recipe on "Default" (or with no choice) gets the backend it was effectively using: the per-kind override
/// for its target kind, else the Settings backend;</item>
/// <item>native search is turned on when the Settings backend was not qBittorrent, or when any recipe ends up on
/// Native/Both.</item>
/// </list>
/// The caller saves the changed recipes, then sets <see cref="NativeSearchSettings.RecipeBackendsMigrated"/> and saves
/// Settings, so a failed save runs the migration again on the next start. The legacy fields stay in settings.json so an
/// older version still reads them.
/// </summary>
public static class SearchBackendMigration
{
    public static SearchBackendMigrationResult Migrate(NativeSearchSettings settings, IReadOnlyList<SearchRecipe> recipes)
    {
        if (settings.RecipeBackendsMigrated)
        {
            return new SearchBackendMigrationResult(false, [], false);
        }

        var changes = new List<RecipeBackendChange>();
        var anyNative = false;
        foreach (var recipe in recipes)
        {
            if (RecipeRuntimeSettings.GetSearchBackend(recipe) is null)
            {
                var effective = settings.BackendByKind.TryGetValue(recipe.TargetKind.ToString(), out var perKind) && !string.IsNullOrWhiteSpace(perKind)
                    ? SearchBackendResolver.Normalize(perKind)
                    : SearchBackendResolver.Normalize(settings.Backend);
                RecipeRuntimeSettings.SetSearchBackend(recipe, effective);
                changes.Add(new RecipeBackendChange(recipe.RecipeId, recipe.Name, recipe.TargetKind.ToString(), effective));
            }

            anyNative |= SearchBackendResolver.UsesNative(SearchBackendResolver.RecipeBackend(recipe));
        }

        var enable = !settings.NativeEnabled &&
                     (anyNative || SearchBackendResolver.UsesNative(SearchBackendResolver.Normalize(settings.Backend)));
        if (enable)
        {
            settings.NativeEnabled = true;
        }

        return new SearchBackendMigrationResult(true, changes, enable);
    }
}

/// <summary>Adds a backend fallback note to an order status line.</summary>
public static class SearchBackendNotes
{
    public static string Append(string? detail, string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return detail ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(detail) && detail.Contains(note, StringComparison.Ordinal))
        {
            return detail;
        }

        return string.IsNullOrWhiteSpace(detail) ? note : $"{detail.TrimEnd()} [{note}]";
    }
}

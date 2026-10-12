using media_management_app.Models;

namespace media_management_app.Services;

public enum RecipeGuardedTermKind
{
    ExcludeTerm,
    BlockedReleaseGroup
}

public sealed record SuspendedRecipeTerm(RecipeGuardedTermKind Kind, string Term, string MatchedTitle);

public sealed class RecipeTermGuardResult
{
    public required SearchRecipe Recipe { get; init; }

    public IReadOnlyList<SuspendedRecipeTerm> Suspended { get; init; } = [];

    public bool HasSuspensions => Suspended.Count > 0;

    public int ExcludeTermCount => Suspended.Count(item => item.Kind == RecipeGuardedTermKind.ExcludeTerm);

    public int BlockedGroupCount => Suspended.Count(item => item.Kind == RecipeGuardedTermKind.BlockedReleaseGroup);

    /// <summary>One line for the console and system log, or null when nothing was suspended.</summary>
    public string? FormatSummary(string recipeName, string mediaTitle)
    {
        if (!HasSuspensions)
        {
            return null;
        }

        var terms = string.Join(", ", Suspended.Select(item => $"'{item.Term}'"));
        return $"Recipe '{recipeName}' for '{mediaTitle}': temporarily suspended {ExcludeTermCount} exclude term(s) and {BlockedGroupCount} blocked group(s) that match the media title ({terms}).";
    }

    /// <summary>Explanation block for the top of a cart debug file, or empty when nothing was suspended.</summary>
    public IReadOnlyList<string> FormatDebugHeader(string mediaTitle)
    {
        if (!HasSuspensions)
        {
            return [];
        }

        var lines = new List<string>
        {
            $"TermGuard: {ExcludeTermCount} exclude term(s) and {BlockedGroupCount} blocked group(s) are suspended for this run of '{mediaTitle}'.",
            "TermGuard: each one matches a title of this media, so it would reject every correct release. The recipe file is not changed; other media still use these terms."
        };
        lines.AddRange(Suspended.Select(item =>
            $"TermGuard: suspended {(item.Kind == RecipeGuardedTermKind.ExcludeTerm ? "exclude term" : "blocked group")} '{item.Term}' (matches title '{item.MatchedTitle}')"));
        return lines;
    }
}

/// <summary>
/// Runs once at the start of a recipe run. Exclude terms and blocked release groups that match one of the
/// media titles (primary, searchable alternatives, recipe aliases, and their sanitized forms) would reject
/// every correct release, so they are removed from a run-scoped copy of the recipe.
/// </summary>
public static class RecipeTermGuard
{
    public static IReadOnlyList<string> CollectTitles(
        SearchRecipe recipe,
        string? primaryTitle,
        IEnumerable<string>? alternativeTitles)
    {
        var raw = new List<string>();
        if (!string.IsNullOrWhiteSpace(primaryTitle))
        {
            raw.Add(primaryTitle);
        }

        if (alternativeTitles is not null)
        {
            raw.AddRange(alternativeTitles.Where(title => !string.IsNullOrWhiteSpace(title)));
        }

        raw.AddRange(recipe.Modules
            .Where(module => module.BlockType == RecipeBlockType.Identity && module.IsEnabled)
            .SelectMany(module => module.Aliases)
            .Where(alias => !string.IsNullOrWhiteSpace(alias)));

        var titles = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var title in raw)
        {
            foreach (var form in new[] { title.Trim(), QueryTemplateRenderer.Sanitize(title) })
            {
                if (!string.IsNullOrWhiteSpace(form) && seen.Add(form))
                {
                    titles.Add(form);
                }
            }
        }

        return titles;
    }

    public static RecipeTermGuardResult Apply(
        SearchRecipe recipe,
        string? primaryTitle,
        IEnumerable<string>? alternativeTitles) =>
        Apply(recipe, CollectTitles(recipe, primaryTitle, alternativeTitles));

    public static RecipeTermGuardResult Apply(SearchRecipe recipe, IReadOnlyList<string> titles)
    {
        var filters = recipe.Modules
            .Where(module => module.BlockType == RecipeBlockType.CandidateFilter)
            .ToList();
        if (filters.Count == 0 || titles.Count == 0)
        {
            return new RecipeTermGuardResult { Recipe = recipe };
        }

        var suspended = new List<SuspendedRecipeTerm>();
        var seen = new HashSet<(RecipeGuardedTermKind, string)>();
        foreach (var filter in filters)
        {
            Collect(filter.ExcludeTerms, RecipeGuardedTermKind.ExcludeTerm);
            Collect(filter.BlockedReleaseGroups, RecipeGuardedTermKind.BlockedReleaseGroup);
        }

        if (suspended.Count == 0)
        {
            return new RecipeTermGuardResult { Recipe = recipe };
        }

        var excluded = suspended
            .Where(item => item.Kind == RecipeGuardedTermKind.ExcludeTerm)
            .Select(item => item.Term)
            .ToHashSet(StringComparer.Ordinal);
        var blocked = suspended
            .Where(item => item.Kind == RecipeGuardedTermKind.BlockedReleaseGroup)
            .Select(item => item.Term)
            .ToHashSet(StringComparer.Ordinal);

        var clone = new SearchRecipe
        {
            RecipeId = recipe.RecipeId,
            Version = recipe.Version,
            MainFlowVersion = recipe.MainFlowVersion,
            Name = recipe.Name,
            TargetKind = recipe.TargetKind,
            Modules = recipe.Modules.Select(module => module.Clone()).ToList()
        };
        foreach (var module in clone.Modules.Where(module => module.BlockType == RecipeBlockType.CandidateFilter))
        {
            module.ExcludeTerms = module.ExcludeTerms
                .Where(term => string.IsNullOrWhiteSpace(term) || !excluded.Contains(term.Trim()))
                .ToList();
            module.BlockedReleaseGroups = module.BlockedReleaseGroups
                .Where(group => string.IsNullOrWhiteSpace(group) || !blocked.Contains(group.Trim()))
                .ToList();
        }

        return new RecipeTermGuardResult { Recipe = clone, Suspended = suspended };

        void Collect(IEnumerable<string> terms, RecipeGuardedTermKind kind)
        {
            foreach (var term in terms.Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term.Trim()))
            {
                if (seen.Contains((kind, term)))
                {
                    continue;
                }

                var matchedTitle = titles.FirstOrDefault(title => ReleaseTermMatcher.Matches(title, term));
                if (matchedTitle is not null)
                {
                    seen.Add((kind, term));
                    suspended.Add(new SuspendedRecipeTerm(kind, term, matchedTitle));
                }
            }
        }
    }
}

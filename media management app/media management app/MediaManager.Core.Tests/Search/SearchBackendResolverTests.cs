using FluentAssertions;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Search;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Search;

/// <summary>
/// The recipe decides the search backend (native-search P6 step 1): Settings only turns native search on or off and
/// holds the kill switch. A recipe set to Native/Both while native is off or paused searches with qBittorrent and says why.
/// </summary>
public class SearchBackendResolverTests
{
    private static SearchRecipe Recipe(string? backend, MediaKind kind = MediaKind.TvEpisode, string name = "Recipe 01")
    {
        var search = new RecipeModuleConfig { BlockType = RecipeBlockType.SearchSource };
        if (backend is not null)
        {
            search.ExtensionData[RecipeRuntimeSettings.SearchBackendKey] = backend;
        }

        return new SearchRecipe { RecipeId = name.Replace(' ', '-'), Name = name, TargetKind = kind, Modules = [search] };
    }

    [Theory]
    [InlineData("Qbittorrent", "Qbittorrent")]
    [InlineData("Native", "Native")]
    [InlineData("both", "Both")]
    [InlineData("Default", "Qbittorrent")]
    [InlineData(null, "Qbittorrent")]
    [InlineData("garbage", "Qbittorrent")]
    public void RecipeChoice_IsTheBackend_WhenNativeIsOn(string? stored, string expected)
    {
        var decision = SearchBackendResolver.Resolve(Recipe(stored), nativeEnabled: true, pauseAll: false);

        decision.Effective.Should().Be(expected);
        decision.FallbackReason.Should().BeNull();
    }

    [Fact]
    public void NoRecipe_SearchesWithQbittorrent()
    {
        SearchBackendResolver.Resolve(null, nativeEnabled: true, pauseAll: false).Effective.Should().Be(SearchBackends.Qbittorrent);
    }

    [Theory]
    [InlineData("Native")]
    [InlineData("Both")]
    public void NativeTurnedOff_FallsBackToQbittorrent_WithAReason(string backend)
    {
        var decision = SearchBackendResolver.Resolve(Recipe(backend), nativeEnabled: false, pauseAll: false);

        decision.Requested.Should().Be(backend);
        decision.Effective.Should().Be(SearchBackends.Qbittorrent);
        decision.FallbackReason.Should().Contain("Recipe 01").And.Contain(backend).And.Contain("turned off").And.Contain("qBittorrent");
    }

    [Fact]
    public void KillSwitch_FallsBackToQbittorrent_WithAReason()
    {
        var decision = SearchBackendResolver.Resolve(Recipe("Native"), nativeEnabled: true, pauseAll: true);

        decision.Effective.Should().Be(SearchBackends.Qbittorrent);
        decision.FallbackReason.Should().Contain("paused");
    }

    [Fact]
    public void QbittorrentRecipe_HasNoFallbackReason_WhateverTheSwitches()
    {
        SearchBackendResolver.Resolve(Recipe("Qbittorrent"), nativeEnabled: false, pauseAll: true).FallbackReason.Should().BeNull();
    }

    [Fact]
    public void LegacySettingsBackend_IsIgnored()
    {
        // Search.Backend / BackendByKind are read once by the migration and never decide a search again.
        var settings = new NativeSearchSettings { Backend = SearchBackends.Native, NativeEnabled = true };
        settings.BackendByKind["TvEpisode"] = SearchBackends.Both;

        SearchBackendResolver.Resolve(Recipe(null), settings.NativeEnabled, settings.PauseAll).Effective.Should().Be(SearchBackends.Qbittorrent);
    }
}

public class SearchBackendMigrationTests
{
    private static SearchRecipe Recipe(string? backend, MediaKind kind, string id)
    {
        var search = new RecipeModuleConfig { BlockType = RecipeBlockType.SearchSource };
        if (backend is not null)
        {
            search.ExtensionData[RecipeRuntimeSettings.SearchBackendKey] = backend;
        }

        return new SearchRecipe { RecipeId = id, Name = $"Recipe {id}", TargetKind = kind, Modules = [search] };
    }

    private static string? Stored(SearchRecipe recipe) => RecipeRuntimeSettings.GetSearchBackend(recipe);

    [Fact]
    public void DefaultRecipes_GetTheEffectiveBackend_PerKindOverrideFirst()
    {
        var settings = new NativeSearchSettings { Backend = SearchBackends.Both };
        settings.BackendByKind["Movie"] = SearchBackends.Native;
        settings.BackendByKind["TvSeasonPack"] = SearchBackends.Qbittorrent;
        var movie = Recipe(null, MediaKind.Movie, "01");
        var episode = Recipe("Default", MediaKind.TvEpisode, "02");
        var pack = Recipe(null, MediaKind.TvSeasonPack, "03");

        var result = SearchBackendMigration.Migrate(settings, [movie, episode, pack]);

        Stored(movie).Should().Be(SearchBackends.Native);
        Stored(episode).Should().Be(SearchBackends.Both);
        Stored(pack).Should().Be(SearchBackends.Qbittorrent);
        result.Changes.Select(change => (change.RecipeId, change.NewBackend)).Should().BeEquivalentTo(
        [
            ("01", SearchBackends.Native),
            ("02", SearchBackends.Both),
            ("03", SearchBackends.Qbittorrent)
        ]);
        result.Ran.Should().BeTrue();
    }

    [Fact]
    public void ExplicitRecipeChoice_IsKept()
    {
        var settings = new NativeSearchSettings { Backend = SearchBackends.Native };
        var recipe = Recipe("Qbittorrent", MediaKind.Movie, "01");

        var result = SearchBackendMigration.Migrate(settings, [recipe]);

        Stored(recipe).Should().Be(SearchBackends.Qbittorrent);
        result.Changes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Native", true)]
    [InlineData("Both", true)]
    [InlineData("Qbittorrent", false)]
    public void NativeIsEnabled_WhenTheSettingsBackendWasNotQbittorrent(string backend, bool expected)
    {
        var settings = new NativeSearchSettings { Backend = backend };

        SearchBackendMigration.Migrate(settings, [Recipe("Qbittorrent", MediaKind.Movie, "01")]);

        settings.NativeEnabled.Should().Be(expected);
    }

    [Fact]
    public void NativeIsEnabled_WhenARecipeEndsUpOnNative()
    {
        // Backend stays Qbittorrent, but the movie override (or an explicit recipe choice) used native: keep doing so.
        var settings = new NativeSearchSettings { Backend = SearchBackends.Qbittorrent };
        settings.BackendByKind["Movie"] = SearchBackends.Native;

        SearchBackendMigration.Migrate(settings, [Recipe(null, MediaKind.Movie, "01")]);

        settings.NativeEnabled.Should().BeTrue();

        var explicitNative = new NativeSearchSettings();
        SearchBackendMigration.Migrate(explicitNative, [Recipe("Both", MediaKind.TvEpisode, "02")]);
        explicitNative.NativeEnabled.Should().BeTrue();
    }

    [Fact]
    public void UnusedPerKindOverride_DoesNotEnableNative()
    {
        var settings = new NativeSearchSettings();
        settings.BackendByKind["Movie"] = SearchBackends.Native;

        SearchBackendMigration.Migrate(settings, [Recipe("Qbittorrent", MediaKind.Movie, "01"), Recipe(null, MediaKind.TvEpisode, "02")]);

        settings.NativeEnabled.Should().BeFalse();
    }

    [Fact]
    public void FreshInstall_EverythingStaysOnQbittorrent()
    {
        var settings = new NativeSearchSettings();
        var recipe = Recipe(null, MediaKind.TvEpisode, "01");

        var result = SearchBackendMigration.Migrate(settings, [recipe]);

        Stored(recipe).Should().Be(SearchBackends.Qbittorrent);
        settings.NativeEnabled.Should().BeFalse();
        result.Changes.Should().ContainSingle();
    }

    [Fact]
    public void RunsOnlyOnce()
    {
        var settings = new NativeSearchSettings { Backend = SearchBackends.Native, RecipeBackendsMigrated = true };
        var recipe = Recipe(null, MediaKind.TvEpisode, "01");

        var result = SearchBackendMigration.Migrate(settings, [recipe]);

        result.Ran.Should().BeFalse();
        result.Changes.Should().BeEmpty();
        Stored(recipe).Should().BeNull();
        settings.NativeEnabled.Should().BeFalse();
    }

    [Fact]
    public void DoesNotMarkItselfDone_TheCallerDoesAfterSaving()
    {
        var settings = new NativeSearchSettings();

        SearchBackendMigration.Migrate(settings, [Recipe(null, MediaKind.Movie, "01")]);

        settings.RecipeBackendsMigrated.Should().BeFalse();
    }

    [Fact]
    public void RecipeWithoutASearchModule_GetsOne()
    {
        var settings = new NativeSearchSettings { Backend = SearchBackends.Native };
        var recipe = new SearchRecipe { RecipeId = "01", TargetKind = MediaKind.Movie, Modules = [] };

        SearchBackendMigration.Migrate(settings, [recipe]);

        Stored(recipe).Should().Be(SearchBackends.Native);
    }

    [Fact]
    public void Describe_NamesEveryChangedRecipe()
    {
        var settings = new NativeSearchSettings { Backend = SearchBackends.Both };
        var result = SearchBackendMigration.Migrate(settings, [Recipe(null, MediaKind.Movie, "01")]);

        result.Describe().Should().Contain(line => line.Contains("Recipe 01") && line.Contains("Both"));
        result.Describe().Should().Contain(line => line.Contains("native search enabled", StringComparison.OrdinalIgnoreCase));
    }
}

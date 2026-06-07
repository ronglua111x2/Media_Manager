using System.Collections.ObjectModel;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using Xunit;

namespace MediaManager.Tests;

public sealed class RecipeServiceTests
{
    [Fact]
    public void GetRecipes_CreatesDefaultRecipeFiles()
    {
        using var folder = new TempFolder();
        var service = new RecipeService(new FakeSettingsService(folder.Path), new NullLogger());

        var recipes = service.GetRecipes();

        Assert.Contains(recipes, recipe => recipe.RecipeId == "default-tv" && recipe.TargetKind == MediaKind.TvEpisode);
        Assert.Contains(recipes, recipe => recipe.RecipeId == "default-movie" && recipe.TargetKind == MediaKind.Movie);
        Assert.True(File.Exists(System.IO.Path.Combine(folder.Path, "Recipes", "default-tv.rcp")));
    }

    [Fact]
    public void SaveRecipe_NormalizesMissingModuleIdentityAndRequiredBlocks()
    {
        using var folder = new TempFolder();
        var service = new RecipeService(new FakeSettingsService(folder.Path), new NullLogger());
        var recipe = new SearchRecipe
        {
            Name = "Tiny",
            TargetKind = MediaKind.TvEpisode,
            Modules =
            [
                new RecipeModuleConfig
                {
                    ModuleId = string.Empty,
                    BlockType = RecipeBlockType.QueryBuilder,
                    Order = 50,
                    QueryTemplates = ["{title} S{season:00}E{episode:00}"]
                }
            ]
        };

        var saved = service.SaveRecipe(recipe);

        Assert.All(saved.Modules, module => Assert.False(string.IsNullOrWhiteSpace(module.ModuleId)));
        Assert.Contains(saved.Modules, module => module.BlockType == RecipeBlockType.Identity);
        Assert.Contains(saved.Modules, module => module.BlockType == RecipeBlockType.LinkOutput);
        Assert.Equal(saved.Modules.OrderBy(module => module.Order).Select(module => module.ModuleId), saved.Modules.Select(module => module.ModuleId));
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"media-manager-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public FakeSettingsService(string stateFolder)
        {
            Current = new AppSettings
            {
                StateFolder = stateFolder,
                AutoTorrent = new AutoTorrentSettings
                {
                    DownloadFolder = stateFolder,
                    CategoryName = "AutoTorrent"
                }
            };
        }

        public AppSettings Current { get; }

        public string SettingsFilePath => System.IO.Path.Combine(Current.StateFolder, "settings.json");

        public void Load()
        {
        }

        public void Save()
        {
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public ObservableCollection<string> UiLogs { get; } = [];

        public void Trace(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        {
        }

        public void Debug(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        {
        }

        public void Info(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        {
        }

        public void Warning(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        {
        }

        public void Error(string message, Exception? exception = null, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        {
        }

        public void Critical(string message, Exception? exception = null, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        {
        }
    }
}

using media_management_app.Common;
using media_management_app.Converters;
using media_management_app.Models;
using media_management_app.ViewModels;
using MediaManager.App.Tests.Cleanup;

namespace MediaManager.App.Tests.ConsoleLog;

public sealed class ConsoleLogPrettyModeTests
{
    [Fact]
    public void Tokenize_SplitsQuotedKeyValueAndNumbers()
    {
        var tokens = LogMessageTokenizer.Tokenize("TMDb best match for 'Frieren' id=209867 confidence=96 in 12 shows.");

        Assert.Contains(new LogToken(LogTokenKind.Quoted, "'Frieren'"), tokens);
        Assert.Contains(new LogToken(LogTokenKind.Key, "id="), tokens);
        Assert.Contains(new LogToken(LogTokenKind.Value, "209867"), tokens);
        Assert.Contains(new LogToken(LogTokenKind.Key, "confidence="), tokens);
        Assert.Contains(new LogToken(LogTokenKind.Number, "12"), tokens);
        Assert.Equal(
            "TMDb best match for 'Frieren' id=209867 confidence=96 in 12 shows.",
            string.Concat(tokens.Select(token => token.Text)));
    }

    [Fact]
    public void Tokenize_DoesNotSplitEpisodeCodesOrWords()
    {
        var tokens = LogMessageTokenizer.Tokenize("Recipe run added S01E05 for Show2");

        Assert.All(tokens, token => Assert.Equal(LogTokenKind.Plain, token.Kind));
    }

    [Fact]
    public void Tokenize_RecognizesUrlsDatesAndQuotedValues()
    {
        var tokens = LogMessageTokenizer.Tokenize("GET https://api.themoviedb.org/3/tv next=2026-11-06 title='A, B' ratio 40/50");

        Assert.Contains(new LogToken(LogTokenKind.Url, "https://api.themoviedb.org/3/tv"), tokens);
        Assert.Contains(new LogToken(LogTokenKind.Value, "2026-11-06"), tokens);
        Assert.Contains(new LogToken(LogTokenKind.Value, "'A, B'"), tokens);
        Assert.Contains(new LogToken(LogTokenKind.Number, "40/50"), tokens);
    }

    [Fact]
    public void ShortSourceName_StripsExtensionAndServiceSuffix()
    {
        Assert.Equal("AutoTrack", ConsoleLogEntryConverter.ShortSourceName("AutoTrackService.cs"));
        Assert.Equal("Service", ConsoleLogEntryConverter.ShortSourceName("Service.cs"));
        Assert.Equal("App.xaml", ConsoleLogEntryConverter.ShortSourceName("App.xaml.cs"));
    }

    [Fact]
    public void ViewModel_FiltersByLevelSearchAndClear()
    {
        var logger = new FakeAppLogger();
        var settings = new FakeSettingsService();
        logger.UiEntries.Add(Entry(1, AppLogLevel.Trace, "trace noise"));
        logger.UiEntries.Add(Entry(2, AppLogLevel.Info, "Auto-track started"));
        logger.UiEntries.Add(Entry(3, AppLogLevel.Error, "TMDb failed"));

        using var viewModel = new ConsoleLogViewModel(logger, settings);

        // Trace is hidden by default.
        Assert.Equal(2, viewModel.VisibleCount);
        Assert.Equal(1, viewModel.ErrorCount);

        viewModel.SearchText = "tmdb";
        viewModel.ApplySearchNow();
        Assert.Equal(1, viewModel.VisibleCount);

        viewModel.SearchText = string.Empty;
        viewModel.ApplySearchNow();
        viewModel.ShowError = false;
        Assert.Equal(1, viewModel.VisibleCount);
        Assert.Contains(AppLogLevel.Error, settings.Current.Logs.ConsoleHiddenLevels);

        viewModel.ClearViewCommand.Execute(null);
        Assert.Equal(0, viewModel.VisibleCount);
        Assert.Equal(0, viewModel.TotalCount);

        logger.UiEntries.Add(Entry(4, AppLogLevel.Info, "after clear"));
        Assert.Equal(1, viewModel.VisibleCount);
    }

    [Fact]
    public void ViewModel_PauseFreezesListAndCountsNewEntries()
    {
        var logger = new FakeAppLogger();
        logger.UiEntries.Add(Entry(1, AppLogLevel.Info, "first"));
        using var viewModel = new ConsoleLogViewModel(logger, new FakeSettingsService());

        viewModel.IsPaused = true;
        logger.UiEntries.Add(Entry(2, AppLogLevel.Info, "second"));
        logger.UiEntries.Add(Entry(3, AppLogLevel.Warning, "third"));

        Assert.Equal(1, viewModel.VisibleCount);
        Assert.Equal(2, viewModel.NewEntryCount);

        viewModel.IsPaused = false;
        Assert.Equal(3, viewModel.VisibleCount);
        Assert.Equal(0, viewModel.NewEntryCount);
    }

    [Fact]
    public void ViewModel_PersistsMode()
    {
        var settings = new FakeSettingsService();
        using var viewModel = new ConsoleLogViewModel(new FakeAppLogger(), settings);

        viewModel.IsPrettyMode = true;

        Assert.Equal(ConsoleViewMode.Pretty, settings.Current.Logs.ConsoleMode);
        Assert.False(viewModel.IsClassicMode);
    }

    private static AppLogEntry Entry(long sequence, AppLogLevel level, string message) => new()
    {
        Sequence = sequence,
        Level = level,
        Message = message,
        SourceFileName = "TestService.cs",
        MemberName = "Run"
    };
}

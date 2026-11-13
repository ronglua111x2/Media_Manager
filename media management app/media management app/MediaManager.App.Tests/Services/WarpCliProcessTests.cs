using System.Diagnostics;
using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

public class WarpCliProcessTests
{
    private const string HangArguments = "/c ping -n 60 127.0.0.1 >nul";

    [Fact]
    public async Task NormalCommand_ReturnsExitCodeAndOutput()
    {
        var result = await WarpCliProcess.RunAsync("cmd.exe", "/c echo hello& exit 3", TimeSpan.FromSeconds(20), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public async Task HungCommand_IsKilledAtTheDeadline()
    {
        var stopwatch = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            WarpCliProcess.RunAsync("cmd.exe", HangArguments, TimeSpan.FromMilliseconds(300), CancellationToken.None));

        Assert.Contains("was killed", ex.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task CallerCancellation_IsReportedAsCancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WarpCliProcess.RunAsync("cmd.exe", HangArguments, TimeSpan.FromSeconds(60), cts.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"took {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData("connect", 20)]
    [InlineData("status", 10)]
    [InlineData("disconnect", 10)]
    [InlineData("-j settings", 10)]
    [InlineData("mode warp+doh", 10)]
    public void DeadlineFor_UsesLongerDeadlineOnlyForConnect(string arguments, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), WarpCliProcess.DeadlineFor(arguments));
    }
}

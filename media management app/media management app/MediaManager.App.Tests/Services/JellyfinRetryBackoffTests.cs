using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

public class JellyfinRetryBackoffTests
{
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 30)]
    [InlineData(3, 120)]
    [InlineData(4, 300)]
    [InlineData(50, 300)]
    public void Next_GrowsAndCapsAtFiveMinutes(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), JellyfinRetryBackoff.Next(failures, warpConnectFailed: false));
    }

    [Fact]
    public void Next_WarpConnectFailed_UsesMaxImmediately()
    {
        Assert.Equal(JellyfinRetryBackoff.Max, JellyfinRetryBackoff.Next(1, warpConnectFailed: true));
    }

    [Fact]
    public void Next_ZeroFailures_IsInitial()
    {
        Assert.Equal(JellyfinRetryBackoff.Initial, JellyfinRetryBackoff.Next(0, warpConnectFailed: false));
    }
}

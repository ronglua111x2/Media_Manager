using System.IO;
using media_management_app.Models;
using media_management_app.Services;
using Xunit;

namespace MediaManager.App.Tests.Services;

/// <summary>
/// PR #12 review: the hunt tunnel is opt-in. Existing settings files have no ForceTunnelForHunt key and must load
/// with it off, so an upgrade does not start switching the whole PC to warp+doh for every hunt.
/// </summary>
public sealed class WarpSettingsDefaultsTests : IDisposable
{
    private readonly string _stateFolder = Path.Combine(Path.GetTempPath(), "mm-warp-defaults-" + Guid.NewGuid().ToString("N"));

    public WarpSettingsDefaultsTests() => Directory.CreateDirectory(_stateFolder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_stateFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void NewSettings_HaveTheHuntTunnelOff()
    {
        Assert.False(new WarpSettings().ForceTunnelForHunt);
        Assert.False(new AppSettings().Warp.ForceTunnelForHunt);
    }

    [Theory]
    [InlineData("""{ "Warp": { "Enabled": true, "AutoRecoverOnSsl": true, "SwitchModeForTmdb": true } }""")]
    [InlineData("""{ "TmdbReadAccessToken": "" }""")]
    public void SettingsFileWithoutTheKey_LoadsWithTheHuntTunnelOff(string json)
    {
        File.WriteAllText(Path.Combine(_stateFolder, "settings.json"), json);
        var service = new SettingsService();

        service.Load(_stateFolder);

        Assert.False(service.Current.Warp.ForceTunnelForHunt);
        Assert.True(service.Current.Warp.SwitchModeForTmdb);
    }

    [Fact]
    public void UserWhoTurnedItOn_KeepsItOn()
    {
        File.WriteAllText(Path.Combine(_stateFolder, "settings.json"), """{ "Warp": { "ForceTunnelForHunt": true } }""");
        var service = new SettingsService();

        service.Load(_stateFolder);

        Assert.True(service.Current.Warp.ForceTunnelForHunt);
    }
}

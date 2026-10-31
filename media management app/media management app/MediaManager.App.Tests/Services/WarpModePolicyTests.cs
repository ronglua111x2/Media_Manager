using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

public class WarpModePolicyTests
{
    // Trimmed from `warp-cli -j settings` on the live machine (2026-10-31).
    private const string DohSettingsJson = """
        {
          "settings": {
            "always_on": false,
            "switch_locked": false,
            "operation_mode": "doh",
            "split_tunnel_mode": "exclude"
          }
        }
        """;

    [Theory]
    [InlineData("warp", true)]
    [InlineData("warp+doh", true)]
    [InlineData("warp+dot", true)]
    [InlineData("doh", false)]
    [InlineData("dot", false)]
    [InlineData("proxy", false)]
    [InlineData("tunnel_only", false)]
    [InlineData(null, false)]
    public void IsTunnelMode(string? mode, bool expected)
    {
        Assert.Equal(expected, WarpModePolicy.IsTunnelMode(mode));
    }

    [Fact]
    public void ParseOperationMode_ReadsLiveJson()
    {
        Assert.Equal("doh", WarpModePolicy.ParseOperationMode(DohSettingsJson));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "settings": {} }""")]
    [InlineData("""{ "status": "Connected" }""")]
    public void ParseOperationMode_MissingOrBroken_IsNull(string json)
    {
        Assert.Null(WarpModePolicy.ParseOperationMode(json));
    }

    [Fact]
    public void ShouldRestore_StillAppliedMode_ReturnsOriginal()
    {
        var marker = new WarpModeRestoreMarker("doh", "warp+doh", DateTime.UtcNow);

        Assert.Equal("doh", WarpModePolicy.ShouldRestore("warp+doh", marker));
    }

    [Fact]
    public void ShouldRestore_UserChangedMode_LeavesIt()
    {
        var marker = new WarpModeRestoreMarker("doh", "warp+doh", DateTime.UtcNow);

        Assert.Null(WarpModePolicy.ShouldRestore("warp", marker));
        Assert.Null(WarpModePolicy.ShouldRestore(null, marker));
    }

    [Fact]
    public void Marker_RoundTrips()
    {
        var marker = new WarpModeRestoreMarker("doh", "warp+doh", new DateTime(2026, 10, 31, 3, 0, 0, DateTimeKind.Utc));

        Assert.Equal(marker, WarpModePolicy.DeserializeMarker(WarpModePolicy.SerializeMarker(marker)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{ "OriginalMode": "", "AppliedMode": "warp+doh" }""")]
    public void DeserializeMarker_Invalid_IsNull(string json)
    {
        Assert.Null(WarpModePolicy.DeserializeMarker(json));
    }

    [Theory]
    [InlineData("doh", true)]
    [InlineData("warp+doh", true)]
    [InlineData("tunnel_only", true)]
    [InlineData("doh; disconnect", false)]
    [InlineData("DOH", false)]
    [InlineData("", false)]
    public void IsValidModeName(string mode, bool expected)
    {
        Assert.Equal(expected, WarpModePolicy.IsValidModeName(mode));
    }

    [Theory]
    [InlineData(WarpLeaseReason.TmdbSslRecover, true)]
    [InlineData(WarpLeaseReason.JellyfinHold, true)]
    [InlineData(WarpLeaseReason.FirstRun, true)]
    [InlineData(WarpLeaseReason.Hunt, false)]
    [InlineData(WarpLeaseReason.User, false)]
    [InlineData(WarpLeaseReason.SettingsTest, false)]
    public void RequiresTunnel(WarpLeaseReason reason, bool expected)
    {
        Assert.Equal(expected, WarpLeaseReasonText.RequiresTunnel(reason));
    }
}

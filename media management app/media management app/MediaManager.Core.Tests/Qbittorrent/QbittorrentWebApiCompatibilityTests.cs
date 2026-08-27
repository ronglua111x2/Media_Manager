using System.Net;
using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Qbittorrent;

public class QbittorrentWebApiCompatibilityTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, "Ok.", true)]
    [InlineData(HttpStatusCode.OK, "Ok", true)]
    [InlineData(HttpStatusCode.NoContent, "", true)]
    [InlineData(HttpStatusCode.NoContent, "   ", true)]
    [InlineData(HttpStatusCode.OK, "Fails.", false)]
    [InlineData(HttpStatusCode.Unauthorized, "", false)]
    [InlineData(HttpStatusCode.Forbidden, "Ok.", false)]
    [InlineData(HttpStatusCode.InternalServerError, "Ok.", false)]
    public void IsLoginSuccess_Matches51And52(HttpStatusCode status, string body, bool expected)
    {
        QbittorrentWebApiCompatibility.IsLoginSuccess(status, body).Should().Be(expected);
    }

    [Fact]
    public void ParseAddResponse_PlainOk_Succeeds()
    {
        var result = QbittorrentWebApiCompatibility.ParseAddResponse(HttpStatusCode.OK, "Ok.");
        result.IsSuccess.Should().BeTrue();
        result.AddedTorrentIds.Should().BeEmpty();
    }

    [Fact]
    public void ParseAddResponse_Empty204_Succeeds()
    {
        var result = QbittorrentWebApiCompatibility.ParseAddResponse(HttpStatusCode.NoContent, "");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ParseAddResponse_PlainFails_Fails()
    {
        var result = QbittorrentWebApiCompatibility.ParseAddResponse(HttpStatusCode.OK, "Fails.");
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ParseAddResponse_Conflict409_Fails()
    {
        var result = QbittorrentWebApiCompatibility.ParseAddResponse(
            HttpStatusCode.Conflict,
            """{"success_count":0,"pending_count":0,"failure_count":1,"added_torrent_ids":[]}""");
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ParseAddResponse_JsonSuccess_ReturnsIds()
    {
        var result = QbittorrentWebApiCompatibility.ParseAddResponse(
            HttpStatusCode.OK,
            """{"success_count":1,"pending_count":0,"failure_count":0,"added_torrent_ids":["abc123"]}""");
        result.IsSuccess.Should().BeTrue();
        result.AddedTorrentIds.Should().Equal("abc123");
    }

    [Fact]
    public void ParseAddResponse_JsonPending202_Succeeds()
    {
        var result = QbittorrentWebApiCompatibility.ParseAddResponse(
            HttpStatusCode.Accepted,
            """{"success_count":0,"pending_count":1,"failure_count":0,"added_torrent_ids":[]}""");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ParseAddResponse_JsonAllFailedOn200_Fails()
    {
        var result = QbittorrentWebApiCompatibility.ParseAddResponse(
            HttpStatusCode.OK,
            """{"success_count":0,"pending_count":0,"failure_count":2,"added_torrent_ids":[]}""");
        result.IsSuccess.Should().BeFalse();
    }
}

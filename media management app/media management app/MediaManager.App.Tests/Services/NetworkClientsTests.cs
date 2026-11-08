using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using media_management_app.Common;
using media_management_app.Services;
using media_management_app.Services.Net;

namespace MediaManager.App.Tests.Services;

public class NetworkClientsTests
{
    [Fact]
    public void Clients_UseTheirDestinationTimeouts()
    {
        using var tmdb = NetworkClients.CreateTmdb(new ConnectionTracker());
        using var shared = NetworkClients.CreateShared();
        using var jellyfin = NetworkClients.CreateJellyfin();
        using var images = NetworkClients.CreateImages();

        Assert.Equal(TimeSpan.FromSeconds(20), tmdb.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(100), shared.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), jellyfin.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), images.Timeout);
    }

    [Fact]
    public async Task TmdbClient_RecordsThePeerAddress_AndDoesNotFollowRedirects()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = AnswerOnceAsync(
            listener,
            "HTTP/1.1 301 Moved Permanently\r\nLocation: http://127.0.0.1:1/never\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var tracker = new ConnectionTracker();
        using var client = NetworkClients.CreateTmdb(tracker);

        using var response = await client.GetAsync($"http://127.0.0.1:{port}/3/configuration");
        await server;

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("127.0.0.1", tracker.LastAddress);
    }

    [Fact]
    public async Task TmdbClient_RecordsDnsAnswer_WhenConnectFails()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var tracker = new ConnectionTracker();
        using var client = NetworkClients.CreateTmdb(tracker);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://127.0.0.1:{port}/"));

        Assert.StartsWith("dns:", tracker.LastAddress);
    }

    [Fact]
    public void FailureLine_HasEverythingNeededToDiagnoseABlock()
    {
        var line = NetFailureLog.Format("tmdb", "api.tmdb.org", "13.224.245.119", 2, 3, NetFailure.TlsFailure, TimeSpan.FromMilliseconds(212), "disconnected/doh");

        Assert.Equal("net.fail client=tmdb host=api.tmdb.org ip=13.224.245.119 attempt=2/3 class=TlsFailure elapsed=212ms warp=disconnected/doh", line);
    }

    private static async Task AnswerOnceAsync(TcpListener listener, string reply)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var buffer = new byte[4096];
        _ = await stream.ReadAsync(buffer);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
    }
}

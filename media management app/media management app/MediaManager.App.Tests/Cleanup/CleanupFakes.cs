using System.Collections.ObjectModel;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Cleanup;

internal sealed class FakeAppLogger : IAppLogger
{
    public ObservableCollection<string> UiLogs { get; } = [];

    public string? ActiveLogFilePath => null;

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

internal sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; } = new();

    public string SettingsFilePath => string.Empty;

    public string? ActiveSettingsLogFilePath => null;

    public bool CreatedNewSettingsThisLoad => false;

    public string BootstrapSource => "test";

    public void Load(string? bootstrapStateFolder = null)
    {
    }

    public void Save()
    {
    }
}

internal sealed class FakeBlacklistService : ITorrentBlacklistService
{
    public bool UrlBlacklisted { get; set; }

    public bool InfoHashBlacklisted { get; set; }

    public int AddCount { get; private set; }

    public Task AddToBlacklistAsync(
        long showId,
        string listingUrl,
        string? infoHash,
        string reason,
        List<SuspiciousFile>? suspiciousFiles = null,
        string? notes = null)
    {
        AddCount++;
        return Task.CompletedTask;
    }

    public bool IsBlacklisted(long showId, string? listingUrl, string? infoHash = null)
    {
        return string.IsNullOrWhiteSpace(infoHash) ? UrlBlacklisted : InfoHashBlacklisted;
    }

    public Task<IReadOnlyList<TorrentBlacklistEntry>> GetBlacklistAsync(long showId)
        => Task.FromResult<IReadOnlyList<TorrentBlacklistEntry>>([]);

    public Task RemoveFromBlacklistAsync(long showId, string listingUrl, string? infoHash = null)
        => Task.CompletedTask;
}

internal sealed class FakeValidationService : ITorrentContentValidationService
{
    public TorrentContentValidationResult Result { get; set; } = new()
    {
        IsValid = true,
        TorrentHash = "hash",
        Recommendation = TorrentHandleRecommendation.Safe
    };

    public Task<TorrentContentValidationResult> ValidateAsync(
        string torrentHash,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result);

    public Task<TorrentContentValidationResult> ValidateFilesAsync(
        string torrentHash,
        IReadOnlyList<TorrentContentFile> files,
        CancellationToken cancellationToken = default,
        string? listingName = null,
        bool isPack = false)
        => Task.FromResult(Result);
}

internal sealed class FakeCleanupService : ITorrentCleanupService
{
    public int DeleteCount { get; private set; }

    public Exception? DeleteException { get; set; }

    public Task<bool> DeleteTorrentAsync(
        string torrentHash,
        bool deleteFiles = true,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        DeleteCount++;
        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        return Task.FromResult(true);
    }

    public Task<bool> IsTorrentDeletedAsync(
        string torrentHash,
        CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

internal sealed class FakeQbittorrentClient : IQbittorrentClient
{
    public int DeleteCalls { get; private set; }

    public int PauseCalls { get; private set; }

    public Exception? DeleteException { get; set; }

    public bool RemainAfterDelete { get; set; }

    public int DeletesBeforeRemoval { get; set; }

    public AddTorrentRequest? LastAdd { get; private set; }

    public AddedTorrentResult Added { get; set; } = new()
    {
        Hash = "abc123",
        Name = "Show.S01E01.mkv",
        State = "downloading"
    };

    public List<TorrentContentFile> Files { get; set; } =
    [
        new() { Name = "Show.S01E01.mkv", Size = 1_000_000 }
    ];

    private readonly HashSet<string> _hashes = new(StringComparer.OrdinalIgnoreCase) { "abc123" };

    public Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<QbittorrentWebUiProbeResult> ProbeWebUiAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(
        TorrentSearchRequest request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<int> StartSearchAsync(TorrentSearchRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<SearchJobResults> GetSearchResultsAsync(
        int searchId,
        int limit,
        int offset = 0,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task StopSearchAsync(int searchId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task DeleteSearchAsync(int searchId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<AddedTorrentResult> AddTorrentAsync(
        AddTorrentRequest request,
        CancellationToken cancellationToken = default)
    {
        LastAdd = request;
        return Task.FromResult(Added);
    }

    public Task<IReadOnlyList<AddedTorrentResult>> GetTorrentsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AddedTorrentResult> list = _hashes
            .Select(hash => new AddedTorrentResult { Hash = hash, Name = Added.Name, State = Added.State })
            .ToList();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<TorrentContentFile>> GetTorrentFilesAsync(
        string hash,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TorrentContentFile>>(Files);

    public Task DeleteTorrentsAsync(
        IEnumerable<string> hashes,
        bool deleteFiles = false,
        CancellationToken cancellationToken = default)
    {
        DeleteCalls++;
        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        if (RemainAfterDelete)
        {
            return Task.CompletedTask;
        }

        if (DeletesBeforeRemoval > 0 && DeleteCalls <= DeletesBeforeRemoval)
        {
            return Task.CompletedTask;
        }

        foreach (var hash in hashes)
        {
            _hashes.Remove(hash);
        }

        return Task.CompletedTask;
    }

    public Task PauseTorrentsAsync(IEnumerable<string> hashes, CancellationToken cancellationToken = default)
    {
        PauseCalls++;
        return Task.CompletedTask;
    }

    public Task ResumeTorrentsAsync(IEnumerable<string> hashes, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<TorrentMetadataProbeResult> ProbeTorrentMetadataAsync(
        TorrentSearchResult result,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<SearchPluginInfo>> GetSearchPluginsAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

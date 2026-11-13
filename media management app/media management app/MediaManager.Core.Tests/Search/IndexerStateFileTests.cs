using FluentAssertions;
using media_management_app.Search;

namespace MediaManager.Core.Tests.Search;

/// <summary>Audit 07d GOV-06 (durable state, backup, cautious start) and GOV-07 (one owner of the state folder).</summary>
public sealed class IndexerStateFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mm-indexer-state-" + Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(_folder, IndexerStateFile.FileName);

    public IndexerStateFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static IndexerStateSnapshot State(string id, int requests) =>
        new() { IndexerId = id, DayKey = "2026-11-13", RequestsToday = requests };

    [Fact]
    public void Save_KeepsThePreviousVersionAsBackup()
    {
        IndexerStateFile.Save(StatePath, [State("knaben", 1)]);
        IndexerStateFile.Save(StatePath, [State("knaben", 2)]);

        IndexerStateFile.Load(StatePath).States.Single().RequestsToday.Should().Be(2);
        File.Exists(StatePath + ".bak").Should().BeTrue();
        IndexerStateFile.Load(StatePath + ".bak").States.Single().RequestsToday.Should().Be(1);
        File.Exists(StatePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void CorruptFile_IsRestoredFromTheBackup()
    {
        IndexerStateFile.Save(StatePath, [State("knaben", 1)]);
        IndexerStateFile.Save(StatePath, [State("knaben", 2)]);
        File.WriteAllBytes(StatePath, new byte[64]);   // what a power cut can leave: zeros

        var loaded = IndexerStateFile.Load(StatePath);

        loaded.Cautious.Should().BeFalse();
        loaded.States.Single().RequestsToday.Should().Be(1);
        loaded.Warning.Should().Contain(".bak");
    }

    [Fact]
    public void FileAndBackupUnreadable_StartCautiously()
    {
        File.WriteAllText(StatePath, "{ not json");
        File.WriteAllText(StatePath + ".bak", string.Empty);

        var loaded = IndexerStateFile.Load(StatePath);

        loaded.Cautious.Should().BeTrue();
        loaded.States.Should().BeEmpty();

        var governor = new IndexerRequestGovernor(TimeProvider.System, _ => new IndexerPolicy(10, 1, TimeSpan.Zero, 1000));
        governor.StartCautiously(["knaben"]);
        governor.GetAvailability("knaben", SearchLane.Background).CanRun.Should().BeFalse();
        governor.GetAvailability("knaben", SearchLane.Interactive).CanRun.Should().BeTrue();
        governor.Remaining("knaben", SearchLane.Interactive).Should().Be(300);
    }

    [Fact]
    public void NoFile_IsAFreshStart_NotACautiousOne()
    {
        var loaded = IndexerStateFile.Load(StatePath);

        loaded.Cautious.Should().BeFalse();
        loaded.Warning.Should().BeNull();
    }

    [Fact]
    public void Lock_IsExclusive_AndReleasedOnDispose()
    {
        using (var first = IndexerStateFile.TryLock(_folder))
        {
            first.Should().NotBeNull();
            IndexerStateFile.TryLock(_folder).Should().BeNull();
        }

        using var again = IndexerStateFile.TryLock(_folder);
        again.Should().NotBeNull();
    }

    [Fact]
    public async Task LastRequestTime_SurvivesARestart_SoTheMinimumIntervalHolds()
    {
        var policy = new IndexerPolicy(10, 1, TimeSpan.FromHours(1), 1000);
        var governor = new IndexerRequestGovernor(TimeProvider.System, _ => policy);
        using (await governor.AcquireAsync("knaben", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None))
        {
        }

        var restarted = new IndexerRequestGovernor(TimeProvider.System, _ => policy, governor.Snapshot());

        var act = () => restarted.AcquireAsync("knaben", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None);
        await act.Should().ThrowAsync<IndexerUnavailableException>().WithMessage("*min interval*");
    }
}

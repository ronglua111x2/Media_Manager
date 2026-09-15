using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Pack;

public class NestedBulkEventCollectorTests
{
    [Fact]
    public void TryQueue_OutsideScope_ReturnsFalse()
    {
        var collector = new NestedBulkEventCollector<string>();

        collector.TryQueue("a").Should().BeFalse();
        collector.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Dispose_OuterScope_RaisesCompletedWithQueuedItems()
    {
        var collector = new NestedBulkEventCollector<string>();
        IReadOnlyList<string>? drained = null;
        collector.Completed += (_, items) => drained = items;

        using (collector.BeginScope())
        {
            collector.IsActive.Should().BeTrue();
            collector.TryQueue("a").Should().BeTrue();
            collector.TryQueue("b").Should().BeTrue();
        }

        drained.Should().Equal("a", "b");
        collector.IsActive.Should().BeFalse();
    }

    [Fact]
    public void NestedScope_DoesNotCompleteUntilOuterDispose()
    {
        var collector = new NestedBulkEventCollector<string>();
        IReadOnlyList<string>? drained = null;
        collector.Completed += (_, items) => drained = items;

        using (collector.BeginScope())
        {
            collector.TryQueue("a").Should().BeTrue();
            using (collector.BeginScope())
            {
                collector.TryQueue("b").Should().BeTrue();
            }

            drained.Should().BeNull();
            collector.IsActive.Should().BeTrue();
        }

        drained.Should().Equal("a", "b");
    }

    [Fact]
    public void Dispose_AfterException_StillCompletes()
    {
        var collector = new NestedBulkEventCollector<string>();
        IReadOnlyList<string>? drained = null;
        collector.Completed += (_, items) => drained = items;

        try
        {
            using (collector.BeginScope())
            {
                collector.TryQueue("kept").Should().BeTrue();
                throw new InvalidOperationException("pack work failed");
            }
        }
        catch (InvalidOperationException)
        {
        }

        drained.Should().Equal("kept");
        collector.IsActive.Should().BeFalse();
    }

    [Fact]
    public void EmptyScope_DoesNotRaiseCompleted()
    {
        var collector = new NestedBulkEventCollector<string>();
        var raised = false;
        collector.Completed += (_, _) => raised = true;

        using (collector.BeginScope())
        {
        }

        raised.Should().BeFalse();
    }

    [Fact]
    public void DoubleDispose_CompletesOnce()
    {
        var collector = new NestedBulkEventCollector<string>();
        var completions = 0;
        IReadOnlyList<string>? drained = null;
        collector.Completed += (_, items) =>
        {
            completions++;
            drained = items;
        };

        var scope = collector.BeginScope();
        collector.TryQueue("a").Should().BeTrue();
        scope.Dispose();
        scope.Dispose();

        completions.Should().Be(1);
        drained.Should().Equal("a");
        collector.TryQueue("late").Should().BeFalse();
    }
}

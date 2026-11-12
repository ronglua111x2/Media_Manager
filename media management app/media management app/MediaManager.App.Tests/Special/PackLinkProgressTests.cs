using media_management_app.ViewModels;

namespace MediaManager.App.Tests.Special;

/// <summary>The AI pack link progress window can be cancelled and never stays stuck after a failure (SPEC-08).</summary>
public class PackLinkProgressTests
{
    [Fact]
    public void Cancel_WhileRunning_CancelsTheRunOnce()
    {
        using var cancellation = new CancellationTokenSource();
        var viewModel = new PackLinkProgressViewModel("Harbor Brass", 1);
        viewModel.AttachCancellation(cancellation);

        Assert.True(viewModel.CanCancel);
        viewModel.CancelCommand.Execute(null);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(viewModel.CanCancel);
    }

    [Fact]
    public void MarkFailed_AfterApplyError_CanBeClosedAndNoLongerCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var viewModel = new PackLinkProgressViewModel("Harbor Brass", 1);
        viewModel.AttachCancellation(cancellation);

        viewModel.MarkFailed("Creating library links failed.", "IOException: disk full");

        Assert.True(viewModel.IsFailed);
        Assert.True(viewModel.CanClose);
        Assert.False(viewModel.CanCancel);
        Assert.False(viewModel.IsComplete);
    }

    [Fact]
    public void PrepareForReview_StopsOfferingCancel()
    {
        using var cancellation = new CancellationTokenSource();
        var viewModel = new PackLinkProgressViewModel("Harbor Brass", 1);
        viewModel.AttachCancellation(cancellation);

        viewModel.PrepareForReview("Opening review...");

        Assert.False(viewModel.CanCancel);
    }

    [Fact]
    public void ResetForRetry_OffersCancelAgain()
    {
        using var cancellation = new CancellationTokenSource();
        var viewModel = new PackLinkProgressViewModel("Harbor Brass", 1);
        viewModel.AttachCancellation(cancellation);
        viewModel.PrepareForReview("Opening review...");

        viewModel.ResetForRetry(isRetry: true);

        Assert.True(viewModel.CanCancel);
    }
}

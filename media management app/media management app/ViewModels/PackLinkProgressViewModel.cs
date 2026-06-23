using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class PackLinkProgressViewModel : ObservableObject
{
    public PackLinkProgressViewModel(string showTitle, int seasonNumber)
    {
        WindowTitle = $"AI Pack Link — {showTitle} S{seasonNumber:00}";
        Steps =
        [
            new PackLinkProgressStepItemViewModel(PackLinkProgressStep.MapRegularEpisodes, "Map regular episodes"),
            new PackLinkProgressStepItemViewModel(PackLinkProgressStep.BuildingPayload, "Build specials payload"),
            new PackLinkProgressStepItemViewModel(PackLinkProgressStep.WaitingForGemini, "Send to Gemini"),
            new PackLinkProgressStepItemViewModel(PackLinkProgressStep.ParsingResponse, "Parse AI response")
        ];
    }

    public string WindowTitle { get; }

    public ObservableCollection<PackLinkProgressStepItemViewModel> Steps { get; }

    [ObservableProperty]
    private string currentMessage = "Starting pack link...";

    [ObservableProperty]
    private string debugDetail = string.Empty;

    [ObservableProperty]
    private bool hasDebugDetail;

    [ObservableProperty]
    private bool canClose;

    [ObservableProperty]
    private bool isComplete;

    [ObservableProperty]
    private bool isFailed;

    [ObservableProperty]
    private bool isCancelled;

    public void Report(PackLinkProgressUpdate update)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Report(update));
            return;
        }

        ApplyUpdate(update);
    }

    public void MarkComplete(string summary)
    {
        Report(new PackLinkProgressUpdate
        {
            Step = PackLinkProgressStep.Complete,
            Status = PackLinkProgressStatus.Done,
            Message = summary
        });
    }

    public void PrepareForReview(string summary)
    {
        IsComplete = true;
        IsFailed = false;
        IsCancelled = false;
        CurrentMessage = summary;
    }

    public void MarkCancelled(string message)
    {
        IsCancelled = true;
        IsComplete = false;
        IsFailed = false;
        CanClose = true;
        CurrentMessage = message;
    }

    public void ResetForRetry(bool isRetry)
    {
        CanClose = false;
        IsComplete = false;
        IsFailed = false;
        IsCancelled = false;
        DebugDetail = string.Empty;
        HasDebugDetail = false;
        CurrentMessage = isRetry ? "Retrying AI mapping..." : "Starting pack link...";

        foreach (var step in Steps)
        {
            step.Status = PackLinkProgressStatus.Pending;
            step.Message = string.Empty;
        }
    }

    public void MarkFailed(string message, string? detail = null)
    {
        foreach (var step in Steps.Where(step => step.Status == PackLinkProgressStatus.Active))
        {
            step.Status = PackLinkProgressStatus.Failed;
        }

        Report(new PackLinkProgressUpdate
        {
            Step = PackLinkProgressStep.Failed,
            Status = PackLinkProgressStatus.Failed,
            Message = message,
            Detail = detail
        });
    }

    public void AllowClose()
    {
        CanClose = true;
    }

    [RelayCommand]
    private void Close(Window? window)
    {
        if (CanClose)
        {
            window?.Close();
        }
    }

    private void ApplyUpdate(PackLinkProgressUpdate update)
    {
        if (!string.IsNullOrWhiteSpace(update.Message))
        {
            CurrentMessage = update.Message;
        }

        if (!string.IsNullOrWhiteSpace(update.Detail))
        {
            DebugDetail = update.Detail;
            HasDebugDetail = true;
        }

        if (update.Step is PackLinkProgressStep.Complete)
        {
            IsComplete = true;
            IsFailed = false;
            CanClose = true;
            CurrentMessage = update.Message;
            return;
        }

        if (update.Step is PackLinkProgressStep.Failed)
        {
            IsFailed = true;
            IsComplete = false;
            CanClose = true;
            CurrentMessage = update.Message;
            if (!string.IsNullOrWhiteSpace(update.Detail))
            {
                DebugDetail = update.Detail;
                HasDebugDetail = true;
            }

            return;
        }

        var stepItem = Steps.FirstOrDefault(step => step.Step == update.Step);
        if (stepItem is null)
        {
            return;
        }

        stepItem.Status = update.Status;
        if (!string.IsNullOrWhiteSpace(update.Message))
        {
            stepItem.Message = update.Message;
        }

        if (update.Status == PackLinkProgressStatus.Failed)
        {
            stepItem.Status = PackLinkProgressStatus.Failed;
            IsFailed = true;
            CanClose = true;
        }
    }
}

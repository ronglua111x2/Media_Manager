namespace media_management_app.Services;

public sealed class OperationProgressService : IOperationProgressService
{
    public int Current { get; private set; }

    public int Total { get; private set; }

    public double Percent { get; private set; }

    public string Message { get; private set; } = "Idle";

    public bool IsActive { get; private set; }

    public event EventHandler? ProgressChanged;

    public void Start(string message, int total)
    {
        Total = Math.Max(total, 0);
        Current = 0;
        Percent = Total == 0 ? 0 : 0;
        Message = message;
        IsActive = true;
        OnProgressChanged();
    }

    public void Report(int current, string message)
    {
        Current = Math.Clamp(current, 0, Total);
        Percent = Total == 0 ? 0 : Current * 100d / Total;
        Message = Total == 0 ? message : $"{message} ({Current}/{Total})";
        IsActive = true;
        OnProgressChanged();
    }

    public void Finish(string message)
    {
        Current = Total;
        Percent = Total == 0 ? 100 : 100;
        Message = message;
        IsActive = false;
        OnProgressChanged();
    }

    private void OnProgressChanged()
    {
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }
}

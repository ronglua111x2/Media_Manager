namespace media_management_app.Services;

public interface IOperationProgressService
{
    int Current { get; }

    int Total { get; }

    double Percent { get; }

    string Message { get; }

    bool IsActive { get; }

    event EventHandler? ProgressChanged;

    void Start(string message, int total);

    void Report(int current, string message);

    void Finish(string message);
}

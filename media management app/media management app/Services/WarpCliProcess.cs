using System.Diagnostics;
using System.Text;

namespace media_management_app.Services;

public readonly record struct WarpCliRunResult(int ExitCode, string Output);

/// <summary>
/// Runs a warp-cli process with a hard deadline. <see cref="WarpCliService"/> holds a global gate while a call
/// runs, so a hung CLI must never be awaited forever; on expiry or cancellation the process tree is killed.
/// </summary>
public static class WarpCliProcess
{
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ConnectDeadline = TimeSpan.FromSeconds(20);

    public static TimeSpan DeadlineFor(string arguments) =>
        string.Equals(arguments.Trim(), "connect", StringComparison.OrdinalIgnoreCase)
            ? ConnectDeadline
            : DefaultDeadline;

    public static async Task<WarpCliRunResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var outputLock = new object();

        void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (outputLock)
            {
                outputBuilder.AppendLine(line);
            }
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCts.CancelAfter(deadline);
        try
        {
            await process.WaitForExitAsync(deadlineCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"{Path.GetFileName(fileName)} {arguments} did not exit within {deadline.TotalSeconds:0.#}s and was killed.");
        }

        lock (outputLock)
        {
            return new WarpCliRunResult(process.ExitCode, outputBuilder.ToString());
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already exited, or it cannot be killed; the caller still gets the timeout.
        }
    }
}

using System.Text.Json;
using media_management_app.Common;

namespace media_management_app.Services.Gemini;

public sealed class GeminiQuotaTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private GeminiQuotaState _state = new();
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public GeminiQuotaTracker(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public int RequestsToday
    {
        get
        {
            EnsureLoaded();
            return _state.RequestsToday;
        }
    }

    public int DailyLimit => AppConstants.GeminiDailyRequestLimit;

    public void EnsureCanRequest()
    {
        EnsureLoaded();
        ResetIfNewDay();
        if (_state.RequestsToday >= AppConstants.GeminiDailyRequestLimit)
        {
            throw new InvalidOperationException(
                $"Gemini daily request limit reached ({AppConstants.GeminiDailyRequestLimit}). Try again tomorrow or switch models.");
        }
    }

    public async Task WaitForSpacingAsync(CancellationToken cancellationToken)
    {
        var elapsed = DateTime.UtcNow - _lastRequestUtc;
        var waitMs = AppConstants.GeminiMinRequestSpacingMs - (int)elapsed.TotalMilliseconds;
        if (waitMs > 0)
        {
            await Task.Delay(waitMs, cancellationToken);
        }
    }

    public void RecordRequest()
    {
        EnsureLoaded();
        ResetIfNewDay();
        _state.RequestsToday++;
        _state.LastRequestUtc = DateTime.UtcNow;
        _lastRequestUtc = _state.LastRequestUtc;
        Save();
    }

    private void EnsureLoaded()
    {
        if (!string.IsNullOrWhiteSpace(_state.DateKey))
        {
            return;
        }

        var path = GetStateFilePath();
        if (!File.Exists(path))
        {
            ResetIfNewDay();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            _state = JsonSerializer.Deserialize<GeminiQuotaState>(json) ?? new GeminiQuotaState();
            ResetIfNewDay();
        }
        catch
        {
            _state = new GeminiQuotaState();
            ResetIfNewDay();
        }
    }

    private void ResetIfNewDay()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (string.Equals(_state.DateKey, today, StringComparison.Ordinal))
        {
            return;
        }

        _state = new GeminiQuotaState
        {
            DateKey = today,
            RequestsToday = 0,
            LastRequestUtc = DateTime.MinValue
        };
        Save();
    }

    private void Save()
    {
        try
        {
            var path = GetStateFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(_state, JsonOptions));
        }
        catch
        {
            // Quota persistence must not block linking.
        }
    }

    private string GetStateFilePath() =>
        Path.Combine(_settingsService.Current.StateFolder, AppConstants.GeminiQuotaStateFileName);

    private sealed class GeminiQuotaState
    {
        public string DateKey { get; set; } = string.Empty;

        public int RequestsToday { get; set; }

        public DateTime LastRequestUtc { get; set; }
    }
}

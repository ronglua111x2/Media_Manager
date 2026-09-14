using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class HostScanRowViewModel : ObservableObject
{
    public HostScanRowViewModel(HostScanRow row)
    {
        Apply(row);
    }

    public HostScanComponentId Id { get; private set; }

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private bool canBrowse;

    [ObservableProperty]
    private string pathDisplay = "—";

    [ObservableProperty]
    private string versionDisplay = "—";

    [ObservableProperty]
    private string requiredLabel = string.Empty;

    [ObservableProperty]
    private string installedLabel = "—";

    [ObservableProperty]
    private string runningLabel = "—";

    [ObservableProperty]
    private string usableLabel = "—";

    [ObservableProperty]
    private string note = string.Empty;

    [ObservableProperty]
    private HostScanSeverity severity;

    [ObservableProperty]
    private string severityLabel = "OK";

    [ObservableProperty]
    private bool showBrowse;

    public void Apply(HostScanRow row)
    {
        Id = row.Id;
        Title = row.Title;
        CanBrowse = row.CanBrowse;
        PathDisplay = string.IsNullOrWhiteSpace(row.Path) ? "—" : row.Path;
        VersionDisplay = string.IsNullOrWhiteSpace(row.DetectedVersion) ? "—" : row.DetectedVersion;
        RequiredLabel = row.RequiredLabel;
        InstalledLabel = Format(row.Installed);
        RunningLabel = Format(row.Running);
        UsableLabel = Format(row.Usable);
        Note = row.Note;
        Severity = row.Severity;
        SeverityLabel = StatusLabel(row);
        ShowBrowse = row.CanBrowse && row.Installed != HostScanCheckStatus.Ok;
    }

    private static string StatusLabel(HostScanRow row)
    {
        if (row.Usable == HostScanCheckStatus.NotConfigured ||
            row.Installed == HostScanCheckStatus.NotConfigured)
        {
            return "Not configured";
        }

        return row.Severity switch
        {
            HostScanSeverity.Error => "Error",
            HostScanSeverity.Warning => "Warn",
            _ => "OK"
        };
    }

    private static string Format(HostScanCheckStatus status) => status switch
    {
        HostScanCheckStatus.Ok => "Yes",
        HostScanCheckStatus.Missing => "No",
        HostScanCheckStatus.NotConfigured => "Not configured",
        HostScanCheckStatus.Failed => "Failed",
        HostScanCheckStatus.Warning => "Warn",
        _ => "—"
    };
}

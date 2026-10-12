namespace media_management_app.Models;

public sealed class PackReconcileResult
{
    public bool Ran { get; init; }

    public bool Skipped { get; init; }

    public string? SkipReason { get; init; }

    public PackTorrentInventory? Inventory { get; init; }

    public AutoTorrentLinkResult? LinkResult { get; init; }

    public string Summary =>
        Skipped
            ? SkipReason ?? "Pack inspect skipped."
            : Inventory?.BuildInspectionDetailText() ?? "Pack inspect complete.";
}

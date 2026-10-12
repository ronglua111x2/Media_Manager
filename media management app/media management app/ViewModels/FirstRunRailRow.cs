using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed class FirstRunRailRow
{
    public bool IsGroupHeader { get; init; }

    public string Title { get; init; } = string.Empty;

    public FirstRunStepKind? Step { get; init; }

    public int PipelineIndex { get; init; } = -1;

    public bool IsCurrent { get; init; }

    public bool IsCompleted { get; init; }

    public bool CanJump { get; init; }

    public bool IsSelectable => CanJump || IsCurrent;
}

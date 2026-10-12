namespace media_management_app.Models;

public enum PackLinkProgressStep
{
    MapRegularEpisodes,
    BuildingPayload,
    WaitingForGemini,
    ParsingResponse,
    ApplyingLinks,
    Complete,
    Failed
}

public enum PackLinkProgressStatus
{
    Pending,
    Active,
    Done,
    Failed
}

namespace media_management_app.Models;

public enum ItemState
{
    Discovered = 0,
    Parsed = 1,
    NeedsReview = 2,
    Linked = 3,
    Error = 4,
    Deleted = 5
}

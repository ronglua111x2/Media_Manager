using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface IMediaImportService
{
    Task<MediaImportPreviewResult> PreviewAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaImportCandidate>> SearchCandidatesAsync(
        MediaKind mediaKind,
        string query,
        CancellationToken cancellationToken = default);

    Task<MediaImportCommitResult> CommitAsync(
        IEnumerable<MediaImportCommitGroup> groups,
        CancellationToken cancellationToken = default);
}

using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class ScannerService : IScannerService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v"
    };

    private readonly IParserService _parserService;
    private readonly IAppLogger _logger;

    public ScannerService(IParserService parserService, IAppLogger logger)
    {
        _parserService = parserService;
        _logger = logger;
    }

    public IReadOnlyList<SourceItem> Scan(IEnumerable<string> sourceFolders)
    {
        var results = new List<SourceItem>();
        var roots = sourceFolders.ToList();
        _logger.Info($"Starting scan for {roots.Count} configured source folder(s)", LogTarget.All);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                _logger.Warning($"Source folder does not exist: {root}", LogTarget.File | LogTarget.Ui | LogTarget.Console);
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (!VideoExtensions.Contains(Path.GetExtension(filePath)))
                {
                    continue;
                }

                var parentFolder = Path.GetDirectoryName(filePath) ?? root;
                var fileName = Path.GetFileName(filePath);
                var scanText = BuildScanText(filePath, root);
                var parsed = _parserService.Parse(fileName, parentFolder);
                var item = new SourceItem
                {
                    SourceRootFolder = root,
                    ParentFolder = parentFolder,
                    FilePath = filePath,
                    FileName = fileName,
                    ScanText = scanText,
                    MediaKind = parsed.MediaKind,
                    ParserPattern = parsed.ParserPattern,
                    ShowTitle = parsed.ShowTitle,
                    MovieTitle = parsed.MovieTitle,
                    MovieYear = parsed.MovieYear,
                    SeasonNumber = parsed.SeasonNumber,
                    EpisodeNumber = parsed.EpisodeNumber,
                    EpisodeTitle = parsed.EpisodeTitle,
                    State = parsed.ParserPattern == ParserPattern.Ignored
                        ? ItemState.Ignored
                        : parsed.NeedsReview ? ItemState.NeedsReview : ItemState.Parsed,
                    Notes = parsed.Reason,
                    LastSeenUtc = DateTime.UtcNow
                };
                results.Add(item);
            }
        }

        _logger.Info($"Scan completed with {results.Count} video item(s)", LogTarget.All);
        return results;
    }

    private static string BuildScanText(string filePath, string root)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty);
        var file = Path.GetFileNameWithoutExtension(filePath);
        if (!string.IsNullOrWhiteSpace(parent) && !string.Equals(parent, Path.GetFileName(root), StringComparison.OrdinalIgnoreCase))
        {
            return $"{parent} {file}";
        }

        return file;
    }
}

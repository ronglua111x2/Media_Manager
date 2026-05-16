using media_management_app.Models;

namespace media_management_app.Services;

public interface IParserService
{
    ParsedCandidate Parse(string fileName, string folderPath);
}

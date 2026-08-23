using System.Text.Json;

namespace MediaManager.Core.Tests.Fixtures;

public static class GoldenFixtureFile
{
    public static string PathTo(string fileName) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    public static JsonElement Root(string fileName)
    {
        var json = File.ReadAllText(PathTo(fileName));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

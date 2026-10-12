using System.Text.Json;

namespace media_management_app.Models;

public sealed class PackContentProfile
{
    public List<int> CoveredSeasons { get; set; } = [];

    public bool IncludesSpecials { get; set; }

    public bool IncludesOva { get; set; }

    public bool IncludesMovies { get; set; }

    public bool IsCompleteBundle { get; set; }

    public List<string> MovieFileNames { get; set; } = [];

    public List<string> SpecialFileNames { get; set; } = [];

    public string TagsDisplay
    {
        get
        {
            var tags = new List<string>();
            if (CoveredSeasons.Count > 0)
            {
                tags.Add(string.Join(",", CoveredSeasons.Select(season => $"S{season:00}")));
            }

            if (IncludesSpecials || IncludesOva)
            {
                tags.Add("Extras/Specials/OVAs");
            }

            if (IncludesMovies)
            {
                tags.Add("Movies");
            }

            if (IsCompleteBundle)
            {
                tags.Add("Complete");
            }

            return string.Join(" · ", tags);
        }
    }

    public string BuildWarningText()
    {
        var lines = new List<string>();
        if (CoveredSeasons.Count > 1)
        {
            lines.Add("Multi-season pack");
        }

        if (IncludesSpecials || IncludesOva)
        {
            var parts = new List<string>();
            if (IncludesSpecials)
            {
                parts.Add("specials");
            }

            if (IncludesOva)
            {
                parts.Add("OVA");
            }

            lines.Add($"Contains {string.Join(" + ", parts)} — verify Extras/Specials/OVAs after link");
        }

        if (IsCompleteBundle)
        {
            lines.Add("Complete/batch release — may include extra content");
        }

        if (IncludesMovies)
        {
            lines.Add("Contains movies — add separately in Movies library (not auto-linked).");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string Serialize(PackContentProfile profile)
    {
        return JsonSerializer.Serialize(profile);
    }

    public static PackContentProfile? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PackContentProfile>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

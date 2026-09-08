using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace media_management_app.Models;

public sealed class CartIntOverride
{
    public bool Enabled { get; set; }

    public int Value { get; set; }
}

public sealed class CartDoubleOverride
{
    public bool Enabled { get; set; }

    public double Value { get; set; }
}

public sealed class CartBoolOverride
{
    public bool Enabled { get; set; }

    public bool Value { get; set; }
}

public sealed class CartRecipeOverrideSet
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public CartIntOverride? MaxCandidates { get; set; }

    public CartIntOverride? MinSeeders { get; set; }

    public CartDoubleOverride? MinSizeGb { get; set; }

    public CartBoolOverride? CandidateDebug { get; set; }

    public bool HasAnyEnabled =>
        MaxCandidates is { Enabled: true } ||
        MinSeeders is { Enabled: true } ||
        MinSizeGb is { Enabled: true } ||
        CandidateDebug is { Enabled: true };

    public RecipeExecutionOverrides? ToExecutionOverrides()
    {
        if (!HasAnyEnabled)
        {
            return null;
        }

        return new RecipeExecutionOverrides
        {
            MaxCandidates = MaxCandidates is { Enabled: true } ? MaxCandidates.Value : null,
            MinSeeders = MinSeeders is { Enabled: true } ? MinSeeders.Value : null,
            OverrideMinSize = MinSizeGb is { Enabled: true },
            MinSizeBytes = MinSizeGb is { Enabled: true } ? GbToBytes(MinSizeGb.Value) : null,
            EnableCandidateDebugLog = CandidateDebug is { Enabled: true } ? CandidateDebug.Value : null
        };
    }

    public static CartRecipeOverrideSet Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CartRecipeOverrideSet();
        }

        try
        {
            return JsonSerializer.Deserialize<CartRecipeOverrideSet>(json, JsonOptions)
                ?? new CartRecipeOverrideSet();
        }
        catch (JsonException)
        {
            return new CartRecipeOverrideSet();
        }
    }

    public static string? Serialize(CartRecipeOverrideSet? set)
    {
        if (set is null ||
            (set.MaxCandidates is null &&
             set.MinSeeders is null &&
             set.MinSizeGb is null &&
             set.CandidateDebug is null))
        {
            return null;
        }

        return JsonSerializer.Serialize(set, JsonOptions);
    }

    public static long? GbToBytes(double gb)
    {
        if (gb <= 0)
        {
            return null;
        }

        return (long)Math.Round(gb * 1024d * 1024d * 1024d);
    }

    public static double BytesToGb(long? bytes)
    {
        if (bytes is null or <= 0)
        {
            return 0;
        }

        return bytes.Value / 1024d / 1024d / 1024d;
    }

    public static string FormatGb(double gb) =>
        gb <= 0
            ? "0"
            : gb.ToString("0.##", CultureInfo.InvariantCulture);

    public static CartRecipeOverrideSet FromLegacyAutoTrackQuality(int? minSeeders, int? minFileSizeMb)
    {
        return new CartRecipeOverrideSet
        {
            MinSeeders = minSeeders is null
                ? null
                : new CartIntOverride { Enabled = true, Value = minSeeders.Value },
            MinSizeGb = minFileSizeMb is > 0
                ? new CartDoubleOverride { Enabled = true, Value = minFileSizeMb.Value / 1024d }
                : null
        };
    }
}

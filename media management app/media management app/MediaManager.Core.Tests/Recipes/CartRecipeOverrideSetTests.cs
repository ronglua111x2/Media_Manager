using FluentAssertions;
using media_management_app.Models;

namespace MediaManager.Core.Tests.Recipes;

public class CartRecipeOverrideSetTests
{
    [Fact]
    public void Parse_MigratedMaxCandidatesJson_EnablesOverride()
    {
        var set = CartRecipeOverrideSet.Parse("""{"maxCandidates":{"enabled":true,"value":7}}""");

        set.MaxCandidates.Should().NotBeNull();
        set.MaxCandidates!.Enabled.Should().BeTrue();
        set.MaxCandidates.Value.Should().Be(7);
        set.ToExecutionOverrides()!.MaxCandidates.Should().Be(7);
    }

    [Fact]
    public void Parse_DisabledOverride_DoesNotAffectRuntime()
    {
        var set = CartRecipeOverrideSet.Parse(
            """{"maxCandidates":{"enabled":false,"value":12},"minSeeders":{"enabled":false,"value":10}}""");

        set.MaxCandidates!.Value.Should().Be(12);
        set.HasAnyEnabled.Should().BeFalse();
        set.ToExecutionOverrides().Should().BeNull();
    }

    [Fact]
    public void Serialize_OmitsUnsetKeys()
    {
        var json = CartRecipeOverrideSet.Serialize(new CartRecipeOverrideSet
        {
            MinSeeders = new CartIntOverride { Enabled = true, Value = 4 }
        });

        json.Should().Contain("minSeeders");
        json.Should().NotContain("maxCandidates");
        json.Should().NotContain("minSizeGb");
        json.Should().NotContain("candidateDebug");
    }

    [Fact]
    public void Parse_CandidateDebug_EnablesOverride()
    {
        var set = CartRecipeOverrideSet.Parse("""{"candidateDebug":{"enabled":true,"value":true}}""");

        set.CandidateDebug.Should().NotBeNull();
        set.CandidateDebug!.Enabled.Should().BeTrue();
        set.CandidateDebug.Value.Should().BeTrue();
        set.ToExecutionOverrides()!.EnableCandidateDebugLog.Should().BeTrue();
    }

    [Fact]
    public void Parse_DisabledCandidateDebug_DoesNotAffectRuntime()
    {
        var set = CartRecipeOverrideSet.Parse("""{"candidateDebug":{"enabled":false,"value":true}}""");

        set.CandidateDebug!.Value.Should().BeTrue();
        set.HasAnyEnabled.Should().BeFalse();
        set.ToExecutionOverrides().Should().BeNull();
    }

    [Fact]
    public void ToExecutionOverrides_CandidateDebugOff_SetsFalse()
    {
        var set = new CartRecipeOverrideSet
        {
            CandidateDebug = new CartBoolOverride { Enabled = true, Value = false }
        };

        var runtime = set.ToExecutionOverrides();
        runtime.Should().NotBeNull();
        runtime!.EnableCandidateDebugLog.Should().BeFalse();
    }

    [Fact]
    public void ToExecutionOverrides_ZeroGb_ClearsSizeFloor()
    {
        var set = new CartRecipeOverrideSet
        {
            MinSizeGb = new CartDoubleOverride { Enabled = true, Value = 0 }
        };

        var runtime = set.ToExecutionOverrides();
        runtime.Should().NotBeNull();
        runtime!.OverrideMinSize.Should().BeTrue();
        runtime.MinSizeBytes.Should().BeNull();
    }

    [Fact]
    public void FromLegacyAutoTrackQuality_ConvertsMegabytesToFractionalGb()
    {
        var set = CartRecipeOverrideSet.FromLegacyAutoTrackQuality(minSeeders: 10, minFileSizeMb: 800);

        set.MinSeeders.Should().NotBeNull();
        set.MinSeeders!.Enabled.Should().BeTrue();
        set.MinSeeders.Value.Should().Be(10);
        set.MinSizeGb.Should().NotBeNull();
        set.MinSizeGb!.Enabled.Should().BeTrue();
        set.MinSizeGb.Value.Should().BeApproximately(800d / 1024d, 0.0001);
        CartRecipeOverrideSet.FormatGb(set.MinSizeGb.Value).Should().Be("0.78");
    }
}

using media_management_app.Services;

namespace MediaManager.App.Tests.Special;

/// <summary>Parsing of the compact Gemini special-mapping response (appendix A, probe A: SPEC-04, SPEC-05).</summary>
public class SpecialResponseParsingTests
{
    [Theory(Skip = "PARSE-20")]
    [InlineData("{\"m\":[[\"0\",\"3\"]],\"w\":[]}")]
    [InlineData("{\"m\":[[0,3.0]],\"w\":[]}")]
    [InlineData("{\"m\":[[null,3]],\"w\":[]}")]
    [InlineData("{\"m\":[[0,99999999999]],\"w\":[]}")]
    [InlineData("[[0,1],[1,2]]")]
    public void TryParseResponse_WrongValueTypes_NeverThrows(string raw)
    {
        var error = Record.Exception(() => SpecialMappingCompactJson.TryParseResponse(raw, out _, out _));

        Assert.Null(error);
    }

    [Theory(Skip = "PARSE-20")]
    [InlineData("{}")]
    [InlineData("{\"error\":\"quota\"}")]
    [InlineData("{\"w\":[\"quota exceeded\"]}")]
    [InlineData("{\"m\":{\"0\":1},\"w\":[]}")]
    [InlineData("[[0,1],[1,2]]")]
    public void TryParseResponse_WithoutMappingArray_Fails(string raw)
    {
        var ok = SpecialMappingCompactJson.TryParseResponse(raw, out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact(Skip = "PARSE-20")]
    public void TryParseResponse_BadPair_IsDroppedWithWarningAndGoodPairsKept()
    {
        var ok = SpecialMappingCompactJson.TryParseResponse(
            "{\"m\":[[0,1],[\"x\",2],[2,3.5],[3,4]],\"w\":[]}",
            out var response,
            out _);

        Assert.True(ok);
        Assert.Equal([(0, 1), (3, 4)], response!.Mappings);
        Assert.NotEmpty(response.Warnings);
    }

    [Theory]
    [InlineData("```json\n{\"m\":[[0,1],[1,2]],\"w\":[]}\n```")]
    [InlineData("Here you go: {\"m\":[[0,1],[1,2]],\"w\":[]} done")]
    [InlineData("[{\"m\":[[0,1],[1,2]],\"w\":[]}]")]
    [InlineData("{\"m\":[[0,1],[1,2]],\"w\":[]}{\"m\":[[0,9]],\"w\":[]}")]
    public void TryParseResponse_WrappedButValidResponse_StillParses(string raw)
    {
        var ok = SpecialMappingCompactJson.TryParseResponse(raw, out var response, out _);

        Assert.True(ok);
        Assert.Equal([(0, 1), (1, 2)], response!.Mappings);
    }
}

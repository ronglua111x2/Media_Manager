using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Search;

public class ReleaseAttributesTests
{
    [Theory]
    [InlineData("Spider-Man.No.Way.Home.2021.2160p.UHD.BluRay.REMUX.DV.HDR10.HEVC.TrueHD.7.1.Atmos-FGT", "Remux,x265,DV,HDR10,Atmos,FGT")]
    [InlineData("Project.Hail.Mary.2026.2160p.iT.WEB-DL.DV.HDR10+.MULTi.DDP5.1.Atmos.H265.MP4-BTM", "WEB-DL,x265,DV,HDR10+,Atmos,BTM")]
    [InlineData("Coherence (2013) 1080p BrRip x264 - YIFY", "BluRay,x264")]
    [InlineData("[SubsPlease] Kanata Kara - 06 (1080p) [ABCDEF12].mkv", "SubsPlease")]
    [InlineData("Coherence 2013 1080p BluRay x265 10bit AAC 5.1-Joy", "BluRay,x265,10bit,AAC,Joy")]
    public void Describe_ExtractsEncodeAttributes(string name, string expected)
    {
        ReleaseAttributes.Describe(name).Should().Equal(expected.Split(','));
    }
}

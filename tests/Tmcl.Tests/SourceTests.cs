using Tmcl.Core;
using Xunit;
namespace Tmcl.Tests;
public sealed class SourceTests
{
    [Theory]
    [InlineData("CN", true)] [InlineData("cn", true)] [InlineData("US", false)] [InlineData("", false)]
    public void ChinaIPv4DefaultsToMirror(string country, bool expected) => Assert.Equal(expected, DownloadSources.DefaultMirror(country));
    [Theory]
    [InlineData(100d, 50d, false)] [InlineData(50d, 100d, true)] [InlineData(null, 100d, true)] [InlineData(100d, null, false)]
    public void RecommendsFastestAvailableSource(double? official, double? mirror, bool expected) =>
        Assert.Equal(expected, DownloadSources.Recommend(new(false, official), new(true, mirror), false));
    [Fact]
    public void FailedMeasurementsPreserveExistingRecommendation() => Assert.True(DownloadSources.Recommend(new(false, null), new(true, null), true));
    [Fact]
    public void ReadsIPv4AndCountry()
    {
        var result = DownloadSources.ParseTrace("fl=123\nip=203.0.113.42\nloc=CN\nhttp=http/2\n");
        Assert.Equal("203.0.113.42", result.IPv4);
        Assert.Equal("CN", result.CountryCode);
    }
    [Fact]
    public void IPv6IsNotMisreportedAsIPv4() => Assert.Throws<IOException>(() => DownloadSources.ParseTrace("ip=2001:db8::1\nloc=CN\n"));
}

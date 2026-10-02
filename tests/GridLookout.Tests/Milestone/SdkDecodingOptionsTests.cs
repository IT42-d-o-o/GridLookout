using GridLookout.Milestone;
using Xunit;

namespace GridLookout.Tests.Milestone;

public class SdkDecodingOptionsTests
{
    [Theory]
    [InlineData(null, "Off")]
    [InlineData("", "Off")]
    [InlineData("Off", "Off")]
    [InlineData("off", "Off")]
    [InlineData(" Auto ", "Auto")]
    [InlineData("AUTO", "Auto")]
    public void HardwareDecoding_KnownValues_NormaliseWithoutWarning(string? input, string expected)
    {
        var warnings = new List<string>();
        Assert.Equal(expected, SdkDecodingOptions.NormalizeHardwareDecoding(input, warnings.Add));
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("On")]
    [InlineData("Nvidia")]
    [InlineData("true")]
    public void HardwareDecoding_UnknownValue_FallsBackToOffWithOneWarning(string input)
    {
        var warnings = new List<string>();
        Assert.Equal("Off", SdkDecodingOptions.NormalizeHardwareDecoding(input, warnings.Add));
        Assert.Single(warnings);
        Assert.Contains(input, warnings[0]);
    }

    [Theory]
    [InlineData(null, "1,1")]
    [InlineData("", "1,1")]
    [InlineData("1,1", "1,1")]
    [InlineData(" 4 , 2 ", "4,2")]
    [InlineData("999,999", "999,999")]
    public void DecodingThreads_ValidValues_Canonicalise(string? input, string expected)
    {
        var warnings = new List<string>();
        Assert.Equal(expected, SdkDecodingOptions.NormalizeDecodingThreads(input, warnings.Add));
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("0,1")]
    [InlineData("1,0")]
    [InlineData("a,b")]
    [InlineData("1000,1")]
    [InlineData("1;1")]
    public void DecodingThreads_InvalidValues_FallBackToDefaultWithOneWarning(string input)
    {
        var warnings = new List<string>();
        Assert.Equal(SdkDecodingOptions.DefaultDecodingThreads, SdkDecodingOptions.NormalizeDecodingThreads(input, warnings.Add));
        Assert.Single(warnings);
    }

    [Fact]
    public void WallConfig_Defaults_AreOffAndOneThread()
    {
        var cfg = new GridLookout.Config.WallConfig();
        Assert.Equal("Off", cfg.HardwareDecoding);
        Assert.Equal("1,1", cfg.SdkDecodingThreads);
    }
}

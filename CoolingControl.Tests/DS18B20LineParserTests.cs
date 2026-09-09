using CoolingControl.DS18B20Plugin;
using Xunit;

namespace CoolingControl.Tests;

public class DS18B20LineParserTests
{
    [Theory]
    [InlineData("t1=+28.70", "t1", 28.70f)]
    [InlineData("t2=+29.20", "t2", 29.20f)]
    [InlineData("t1=-5.00", "t1", -5.00f)]
    [InlineData("t10=+100.5", "t10", 100.5f)]
    [InlineData("t1=28.70", "t1", 28.70f)]
    [InlineData("T3=+12.34", "t3", 12.34f)]
    public void TryParse_ValidLine_ReturnsTagAndValue(string line, string expectedTag, float expectedValue)
    {
        var result = LineParser.TryParse(line, out var tag, out var value);

        Assert.True(result);
        Assert.Equal(expectedTag, tag);
        Assert.Equal(expectedValue, value, precision: 3);
    }

    [Theory]
    [InlineData("t1=+28.70\r")]
    [InlineData("t1=+28.70\r\n")]
    [InlineData("  t1=+28.70  ")]
    public void TryParse_TrimsWhitespaceAndLineEndings(string line)
    {
        var result = LineParser.TryParse(line, out var tag, out var value);

        Assert.True(result);
        Assert.Equal("t1", tag);
        Assert.Equal(28.70f, value, precision: 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("t1=")]
    [InlineData("=28.70")]
    [InlineData("t=28.70")]
    [InlineData("t1==28.70")]
    [InlineData("t1:28.70")]
    [InlineData("t1=28.70abc")]
    public void TryParse_InvalidLine_ReturnsFalse(string? line)
    {
        var result = LineParser.TryParse(line, out var tag, out var value);

        Assert.False(result);
        Assert.Equal(string.Empty, tag);
        Assert.Equal(0f, value);
    }
}

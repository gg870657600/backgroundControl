using backgroundControl.Tools;

public class IperfParserTests
{
    [Theory]
    [InlineData("1", "bits/sec", 1)]
    [InlineData("1", "kbits/sec", 1000)]
    [InlineData("1", "mbits/sec", 1000000)]
    [InlineData("1", "gbits/sec", 1000000000)]
    [InlineData("10.5", "mbits/sec", 10500000)]
    [InlineData("0", "bits/sec", 0)]
    public void ParseBitsPerSec_ConvertsCorrectly(string value, string unit, double expected)
    {
        var result = IperfRunner.ParseBitsPerSec(value, unit);

        result.Should().Be(expected);
    }
}

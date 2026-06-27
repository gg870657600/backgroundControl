using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using backgroundControl.Tools;
using FluentAssertions;

public class IperfPerformanceTests
{
    private string GenerateIperfOutput(int intervalCount, bool includeFinal = true)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < intervalCount; i++)
        {
            sb.AppendLine($"[  4]  {i}.00-{i + 1}.00  sec   100 MBytes   839 Mbits/sec    0.234 ms    0/100000 (0.0%)");
        }
        if (includeFinal)
        {
            sb.AppendLine($"[  4]  0.00-{intervalCount}.00  sec   {intervalCount * 100} MBytes   {intervalCount * 839} Mbits/sec                  sender");
            sb.AppendLine($"[  4]  0.00-{intervalCount}.00  sec   {intervalCount * 100} MBytes   {intervalCount * 838} Mbits/sec                  receiver");
        }
        return sb.ToString();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Parse1000Intervals_Under100ms()
    {
        var output = GenerateIperfOutput(1000);
        var runner = new IperfRunner();

        var sw = Stopwatch.StartNew();
        runner.ParseFinalResult(output);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Parse5000Intervals_Under200ms()
    {
        var output = GenerateIperfOutput(5000);
        var runner = new IperfRunner();

        var sw = Stopwatch.StartNew();
        runner.ParseFinalResult(output);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(200);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ParseBitsPerSec_VariousUnits_AllCorrect()
    {
        var testCases = new (string Value, string Unit, double Expected)[]
        {
            ("1", "bits/sec", 1),
            ("1", "kbits/sec", 1000),
            ("1", "mbits/sec", 1000000),
            ("1", "gbits/sec", 1000000000),
            ("100.5", "mbits/sec", 100500000),
        };

        foreach (var (val, unit, expected) in testCases)
        {
            var result = IperfRunner.ParseBitsPerSec(val, unit);
            result.Should().Be(expected, $"ParseBitsPerSec({val}, {unit}) should be {expected}");
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void TryParseTextInterval_ParsesAllLines_Rapid()
    {
        var runner = new IperfRunner();
        var output = GenerateIperfOutput(500);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var sw = Stopwatch.StartNew();
        foreach (var line in lines)
        {
            runner.TryParseTextInterval(line);
        }
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void IntervalRegex_MatchesAllSampleLines()
    {
        var samples = new[]
        {
            "[  4]  0.00-1.00  sec   100 MBytes   839 Mbits/sec    0.234 ms    0/100000 (0.0%)",
            "[  5]  1.00-2.00  sec   1.23 GBytes   10.5 Gbits/sec    1.234 ms    1/100000 (0.001%)",
            "[  4]  0.00-1.00  sec   100 KBytes   819 Kbits/sec",
        };

        var regex = new Regex(
            @"\[\s*\d+\]\s+([\d.]+)-([\d.]+)\s+sec\s+([\d.]+)\s+(\w+)\s+([\d.]+)\s+(\w+/sec)",
            RegexOptions.Compiled);

        foreach (var sample in samples)
        {
            var match = regex.Match(sample);
            match.Success.Should().BeTrue($"Regex should match: {sample}");
        }
    }
}

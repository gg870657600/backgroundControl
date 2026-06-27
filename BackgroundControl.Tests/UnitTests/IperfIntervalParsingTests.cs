using backgroundControl.Tools;

public class IperfIntervalParsingTests
{
    [Fact]
    public void ParseInterval_ServerMode_ParsesReceiverBitsPerSec()
    {
        var runner = new IperfRunner();
        IperfIntervalData? captured = null;
        runner.OnInterval += data => captured = data;

        runner.TryParseTextInterval("[  5] 1.00-2.00 sec 1.25 MBytes 10.5 Mbits/sec");

        captured.Should().NotBeNull();
        captured!.ReceiverBitsPerSec.Should().Be(10500000);
    }

    [Fact]
    public void ParseInterval_ServerReceiver_ParsesReceiverBitsPerSec()
    {
        var runner = new IperfRunner();
        IperfIntervalData? captured = null;
        runner.OnInterval += data => captured = data;

        // Simulate server mode by calling with a line that doesn't say "sender"
        // Actually the runner is always created as client by default.
        // We'll test the receiver scenario by using the "receiver" keyword
        runner.TryParseTextInterval("[  5] 1.00-2.00 sec 1.25 MBytes 10.5 Mbits/sec receiver");

        captured.Should().NotBeNull();
        captured!.ReceiverBitsPerSec.Should().Be(10500000);
    }

    [Fact]
    public void ParseInterval_UdpJitter_ParsesJitterMs()
    {
        var runner = new IperfRunner();
        IperfIntervalData? captured = null;
        runner.OnInterval += data => captured = data;

        runner.TryParseTextInterval("[  3] 0.00-1.00 sec 1.00 MBytes 8.39 Mbits/sec 0.123 ms 0/1000 (0%)");

        captured.Should().NotBeNull();
        captured!.JitterMs.Should().Be(0.123);
    }

    [Fact]
    public void ParseInterval_UdpLoss_ParsesLoss()
    {
        var runner = new IperfRunner();
        IperfIntervalData? captured = null;
        runner.OnInterval += data => captured = data;

        runner.TryParseTextInterval("[  3] 0.00-1.00 sec 1.00 MBytes 8.39 Mbits/sec 0.500 ms 123/456 (27.0%)");

        captured.Should().NotBeNull();
        captured!.LostPackets.Should().Be(123);
        captured.TotalPackets.Should().Be(456);
        captured.LossPercent.Should().Be(27.0);
    }

    [Fact]
    public void ParseInterval_NonIntervalLine_Ignored()
    {
        var runner = new IperfRunner();
        bool eventFired = false;
        runner.OnInterval += data => eventFired = true;

        runner.TryParseTextInterval("Connecting to host 192.168.1.1, port 5201");

        eventFired.Should().BeFalse();
    }

    [Fact]
    public void ParseFinalResult_ExtractsSenderAndReceiver()
    {
        var runner = new IperfRunner();
        IperfFinalResult? captured = null;
        runner.OnFinal += data => captured = data;

        string output = @"
[ ID] Interval           Transfer     Bitrate
[  5] 0.00-10.00 sec  12.5 MBytes  10.5 Mbits/sec                  sender
[  5] 0.00-10.00 sec  12.4 MBytes  10.4 Mbits/sec                  receiver
";
        runner.ParseFinalResult(output);

        captured.Should().NotBeNull();
        captured!.SenderMbps.Should().BeApproximately(10.5, 0.01);
        captured.ReceiverMbps.Should().BeApproximately(10.4, 0.01);
    }

    [Fact]
    public void ParseFinalResult_OnlySender_ReturnsPartial()
    {
        var runner = new IperfRunner();
        IperfFinalResult? captured = null;
        runner.OnFinal += data => captured = data;

        string output = @"
[  5] 0.00-10.00 sec  12.5 MBytes  10.5 Mbits/sec                  sender
";
        runner.ParseFinalResult(output);

        captured.Should().NotBeNull();
        captured!.SenderMbps.Should().BeApproximately(10.5, 0.01);
        captured.ReceiverMbps.Should().Be(0);
    }

    [Fact]
    public void ParseFinalResult_NoMatch_NoEvent()
    {
        var runner = new IperfRunner();
        bool eventFired = false;
        runner.OnFinal += data => eventFired = true;

        runner.ParseFinalResult("some random output without interval lines");

        eventFired.Should().BeFalse();
    }
}

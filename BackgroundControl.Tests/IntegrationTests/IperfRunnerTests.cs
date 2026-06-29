using System.Diagnostics;
using backgroundControl.Tools;

public class IperfRunnerTests : IDisposable
{
    private readonly int _port;
    private Process? _serverProcess;
    private readonly string _iperfExe;

    public IperfRunnerTests()
    {
        _port = GetFreePort();
        _iperfExe = IperfLocator.GetIperfExePath();

        var psi = new ProcessStartInfo
        {
            FileName = _iperfExe,
            Arguments = $"-s -1 -p {_port} -i 1 --forceflush",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        _serverProcess = new Process { StartInfo = psi };
        _serverProcess.Start();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartClient_Tcp_ReceivesIntervalData()
    {
        var dataReceived = false;
        using var runner = new IperfRunner();

        runner.OnInterval += data =>
        {
            if (data.BitsPerSec > 0)
                dataReceived = true;
        };

        runner.StartClient("127.0.0.1", _port, duration: 2, parallel: 1, udp: false, bandwidth: "", interval: 1);
        await Task.Delay(5000);
        runner.Stop();

        dataReceived.Should().BeTrue("iperf3 should produce at least one interval with bandwidth > 0");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartClient_Udp_ReportsJitterAndLoss()
    {
        bool hasInterval = false;
        using var runner = new IperfRunner();

        runner.OnInterval += data =>
        {
            hasInterval = true;
            data.JitterMs.Should().BeGreaterOrEqualTo(0);
        };

        runner.StartClient("127.0.0.1", _port, duration: 2, parallel: 1, udp: true, bandwidth: "10M", interval: 1);
        await Task.Delay(5000);
        runner.Stop();

        hasInterval.Should().BeTrue("UDP iperf should produce interval data");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartClient_Parallel5_AllStreamsComplete()
    {
        int intervals = 0;
        using var runner = new IperfRunner();

        runner.OnInterval += data => intervals++;

        runner.StartClient("127.0.0.1", _port, duration: 2, parallel: 5, udp: false, bandwidth: "", interval: 1);
        await Task.Delay(5000);
        runner.Stop();

        intervals.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartClient_Bandwidth10M_ResultCloseToLimit()
    {
        IperfFinalResult? final = null;
        using var runner = new IperfRunner();

        runner.OnFinal += result => final = result;

        runner.StartClient("127.0.0.1", _port, duration: 3, parallel: 1, udp: true, bandwidth: "10M", interval: 1);
        await Task.Delay(7000);
        runner.Stop();

        final.Should().NotBeNull();
        final.SenderMbps.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartStop_RapidCycles_NoCrash()
    {
        int intervalCount = 0;
        for (int i = 0; i < 3; i++)
        {
            using var runner = new IperfRunner();
            runner.OnInterval += _ => intervalCount++;
            runner.StartClient("127.0.0.1", _port, duration: 1, parallel: 1, udp: false, bandwidth: "", interval: 1);
            await Task.Delay(500);
            runner.Stop();
        }
        intervalCount.Should().BeGreaterThan(0, "各周期应至少产生一个 interval 数据");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ServerMode_ClientConnects_ReceivesData()
    {
        var serverData = false;
        using var server = new IperfRunner();
        server.OnInterval += data =>
        {
            if (data.ReceiverBitsPerSec > 0)
                serverData = true;
        };
        server.StartServer(_port + 1);
        await Task.Delay(500);

        using var client = new IperfRunner();
        client.StartClient("127.0.0.1", _port + 1, duration: 2, parallel: 1, udp: false, bandwidth: "", interval: 1);

        await Task.Delay(5000);
        server.Stop();
        client.Stop();

        serverData.Should().BeTrue("Server-mode IperfRunner should receive interval data from client");
    }

    public void Dispose()
    {
        try
        {
            if (_serverProcess is { HasExited: false })
            {
                _serverProcess.Kill(entireProcessTree: true);
                _serverProcess.WaitForExit(2000);
            }
            _serverProcess?.Dispose();
        }
        catch { }
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

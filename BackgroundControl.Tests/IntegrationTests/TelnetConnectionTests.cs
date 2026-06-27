using System.Net;
using System.Net.Sockets;
using System.Text;

public class TelnetConnectionTests : IDisposable
{
    private readonly TcpListener _listener;
    private readonly int _port;
    private Task? _serverTask;
    private readonly CancellationTokenSource _cts = new();

    public TelnetConnectionTests()
    {
        _port = GetFreePort();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();

        _serverTask = Task.Run(async () =>
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                var stream = client.GetStream();
                var buf = new byte[1024];

                while (!_cts.Token.IsCancellationRequested)
                {
                    var n = await stream.ReadAsync(buf, 0, buf.Length, _cts.Token);
                    if (n == 0) break;
                    var data = Encoding.UTF8.GetString(buf, 0, n);
                    if (data.Contains("get-ne-type"))
                    {
                        var resp = Encoding.UTF8.GetBytes("E600X\r\n");
                        await stream.WriteAsync(resp, 0, resp.Length, _cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        });
    }

    [Fact]
    public async Task TelnetConnectAndSend_ReceivesResponse()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var cmd = Encoding.UTF8.GetBytes("get-ne-type\r\n");
        await stream.WriteAsync(cmd, 0, cmd.Length);

        var buf = new byte[1024];
        using var readCts = new CancellationTokenSource(3000);
        var n = await stream.ReadAsync(buf, 0, buf.Length, readCts.Token);
        var response = Encoding.UTF8.GetString(buf, 0, n);

        response.Trim().Should().Be("E600X");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

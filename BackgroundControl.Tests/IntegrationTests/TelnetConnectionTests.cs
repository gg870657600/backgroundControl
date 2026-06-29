using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using backgroundControl.Tools;
using Xunit.Sdk;

public class TelnetConnectionTests : IDisposable
{
    private readonly TcpClient? _client;
    private readonly string _ip;
    private readonly int _port;
    private readonly string _user;
    private readonly string _pass;
    private bool _connected;

    public TelnetConnectionTests()
    {
        var recent = SshHistoryManager.Load()
            .Where(e => e.ConnectionType == "Telnet")
            .MaxBy(e => e.LastUsed);

        if (recent == null)
        {
            _connected = false;
            return;
        }

        _ip = recent.Ip;
        _port = recent.Port;
        _user = recent.Username;
        _pass = SshHistoryManager.DecryptPassword(recent.Password);

        try
        {
            _client = new TcpClient();
            _client.ConnectAsync(_ip, _port).Wait(TimeSpan.FromSeconds(5));
            _connected = _client.Connected;
        }
        catch
        {
            _connected = false;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TelnetConnectAndSend_ReceivesResponse()
    {
        if (!_connected) throw new SkipTestException("设备未连接");

        var stream = _client!.GetStream();
        stream.ReadTimeout = 3000;
        stream.WriteTimeout = 3000;

        var buf = new byte[4096];
        try { await stream.ReadAsync(buf, 0, buf.Length); } catch (Exception ex) { Debug.WriteLine($"协商读取（可忽略）: {ex.Message}"); }

        var loginBytes = Encoding.UTF8.GetBytes($"login:{_user},{_pass}\r\n");
        await stream.WriteAsync(loginBytes, 0, loginBytes.Length);
        await Task.Delay(1500);

        try { await stream.ReadAsync(buf, 0, buf.Length); } catch (Exception ex) { Debug.WriteLine($"登录输出读取（可忽略）: {ex.Message}"); }

        var cmd = Encoding.UTF8.GetBytes("get-ne-type\r\n");
        await stream.WriteAsync(cmd, 0, cmd.Length);
        await Task.Delay(3000);

        var respBuf = new byte[8192];
        var n = 0;
        try { n = await stream.ReadAsync(respBuf, 0, respBuf.Length); } catch (Exception ex) { Debug.WriteLine($"命令响应读取: {ex.Message}"); }

        var response = Encoding.UTF8.GetString(respBuf, 0, n);
        response.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectToNonexistentDevice_Fails()
    {
        using var client = new TcpClient();
        var ex = await Record.ExceptionAsync(() => client.ConnectAsync("192.168.1.234", 23)
            .WaitAsync(TimeSpan.FromSeconds(5)));
        ex.Should().NotBeNull();
    }

    public void Dispose()
    {
        try { _client?.Close(); } catch { }
    }
}

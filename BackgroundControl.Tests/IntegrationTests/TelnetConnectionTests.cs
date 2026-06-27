using System.Net;
using System.Net.Sockets;
using System.Text;
using backgroundControl.Tools;

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
        if (!_connected) return;

        var stream = _client!.GetStream();
        stream.ReadTimeout = 3000;
        stream.WriteTimeout = 3000;

        // 丢弃 telnet 协商字节（0xFF 开头的 IAC 序列）
        var buf = new byte[4096];
        try { await stream.ReadAsync(buf, 0, buf.Length); } catch { }

        // 发送 login:user,pass（大部分华为/中兴 ONU 设备支持这种一行登录格式）
        var loginBytes = Encoding.UTF8.GetBytes($"login:{_user},{_pass}\r\n");
        await stream.WriteAsync(loginBytes, 0, loginBytes.Length);
        await Task.Delay(1500);

        // 再次丢弃登录后的提示输出
        try { await stream.ReadAsync(buf, 0, buf.Length); } catch { }

        // 发送命令
        var cmd = Encoding.UTF8.GetBytes("get-ne-type\r\n");
        await stream.WriteAsync(cmd, 0, cmd.Length);
        await Task.Delay(3000);

        // 读取命令输出
        var respBuf = new byte[8192];
        var n = 0;
        try { n = await stream.ReadAsync(respBuf, 0, respBuf.Length); } catch { }

        var response = Encoding.UTF8.GetString(respBuf, 0, n);
        response.Should().NotBeNullOrEmpty();
    }

    public void Dispose()
    {
        try { _client?.Close(); } catch { }
    }
}

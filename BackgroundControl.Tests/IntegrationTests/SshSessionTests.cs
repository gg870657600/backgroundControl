using System.Diagnostics;
using Renci.SshNet;
using backgroundControl.Tools;

public class SshSessionTests : IDisposable
{
    private readonly SshClient? _client;
    private readonly ShellStream? _shell;
    private readonly string _ip;
    private readonly int _port;
    private readonly string _user;
    private readonly string _pass;
    private bool _connected;

    public SshSessionTests()
    {
        _ip = "197.6.6.26";
        _port = 22;
        _user = "root";
        _pass = "andisat";

        try
        {
            var ci = new ConnectionInfo(_ip, _port, _user,
                new PasswordAuthenticationMethod(_user, _pass));
            ci.Timeout = TimeSpan.FromSeconds(5);
            _client = new SshClient(ci);
            _client.Connect();
            _connected = _client.IsConnected;

            if (_connected)
            {
                _shell = _client.CreateShellStream("xterm-256color", 80, 24, 800, 600, 1024 * 1024);
                _shell.WriteLine("export TMOUT=0");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SSH setup failed: {ex.Message}");
            _connected = false;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ConnectToDevice_IsConnected()
    {
        _connected.Should().BeTrue("SSH connection to device at {0}:{1} should succeed", _ip, _port);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectToDevice_ShellStream_CanRead()
    {
        if (!_connected) return;

        _shell.WriteLine("echo ssh-test-ok");
        await Task.Delay(1000);
        var output = _shell.Read();
        output.Should().Contain("ssh-test-ok");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LinuxCommand_Ls_ExecutesViaSsh()
    {
        if (!_connected) return;

        _shell.WriteLine("ls /");
        await Task.Delay(1000);
        var output = _shell.Read();
        output.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SwitchToTelnet_SendsCommandAndGetsResponse()
    {
        if (!_connected) return;

        _shell.WriteLine("telnet 0 2323");
        await Task.Delay(2000);
        _shell.WriteLine("login:root,Changeme_123");
        await Task.Delay(1000);
        _shell.WriteLine("get-ne-type");
        await Task.Delay(2000);
        var output = _shell.Read();

        output.Should().Contain("E600");

        _shell.WriteLine("\x03");
        await Task.Delay(500);
        _shell.WriteLine("e");
        await Task.Delay(500);
    }

    public void Dispose()
    {
        try { _shell?.Dispose(); } catch { }
        if (_client != null) { try { _client.Disconnect(); } catch { } try { _client.Dispose(); } catch { } }
    }
}

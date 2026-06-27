using System.Net;
using backgroundControl.Tools;

public class FtpServerTests : IDisposable
{
    private readonly FtpServerHost _server;
    private readonly string _tempDir;
    private readonly int _port;

    public FtpServerTests()
    {
        _port = GetFreePort();
        _tempDir = Path.Combine(Path.GetTempPath(), "FtpServerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "hello world");

        var cfg = new FtpConfig
        {
            Port = _port,
            RootDir = _tempDir,
            AllowAnonymous = true,
            PassiveStart = _port + 1,
            PassiveEnd = _port + 10,
        };

        _server = new FtpServerHost(cfg);
        _server.OnLog += (msg) => { };
        _server.Start();
    }

    [Fact]
    public void ServerStartsAndStops()
    {
        _server.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void GetRootFullPath_ReturnsNormalized()
    {
        var result = FtpServerHost.GetRootFull(_tempDir);
        result.Should().EndWith(Path.DirectorySeparatorChar.ToString());
    }

    [Fact]
    public void IsInsideRoot_SamePath_ReturnsTrue()
    {
        var root = FtpServerHost.GetRootFull(_tempDir);
        FtpServerHost.IsInsideRoot(root, _tempDir).Should().BeTrue();
    }

    [Fact]
    public void IsInsideRoot_SubPath_ReturnsTrue()
    {
        var root = FtpServerHost.GetRootFull(_tempDir);
        FtpServerHost.IsInsideRoot(root + "sub", _tempDir).Should().BeTrue();
    }

    public void Dispose()
    {
        _server.Stop();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LoginAnonymous_Returns230()
    {
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, _port);
        var stream = client.GetStream();
        var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
        var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        var banner = await reader.ReadLineAsync();
        banner.Should().StartWith("220");

        await writer.WriteLineAsync("USER anonymous");
        var resp1 = await reader.ReadLineAsync();
        resp1.Should().StartWith("230");

        await writer.WriteLineAsync("QUIT");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Stor_UploadFile_WritesToDisk()
    {
        using var ctrl = new System.Net.Sockets.TcpClient();
        await ctrl.ConnectAsync(System.Net.IPAddress.Loopback, _port);
        var stream = ctrl.GetStream();
        var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
        var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        await reader.ReadLineAsync(); // banner
        await writer.WriteLineAsync("USER anonymous");
        await reader.ReadLineAsync();

        await writer.WriteLineAsync("PASV");
        var pasvResp = await reader.ReadLineAsync();
        pasvResp.Should().StartWith("227");

        var parts = pasvResp.Split('(', ')')[1].Split(',');
        int dataPort = int.Parse(parts[4]) * 256 + int.Parse(parts[5]);

        using var data = new System.Net.Sockets.TcpClient();
        await data.ConnectAsync(System.Net.IPAddress.Loopback, dataPort);

        await writer.WriteLineAsync("STOR stor-test.txt");
        var storResp = await reader.ReadLineAsync();
        storResp.Should().StartWith("150");

        var dataStream = data.GetStream();
        var uploadBytes = Encoding.UTF8.GetBytes("stor content");
        await dataStream.WriteAsync(uploadBytes, 0, uploadBytes.Length);
        dataStream.Close();

        var finalResp = await reader.ReadLineAsync();
        finalResp.Should().StartWith("226");

        var uploaded = Path.Combine(_tempDir, "stor-test.txt");
        File.Exists(uploaded).Should().BeTrue();
        File.ReadAllText(uploaded).Should().Be("stor content");

        await writer.WriteLineAsync("QUIT");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Retr_DownloadFile_ReturnsContent()
    {
        using var ctrl = new System.Net.Sockets.TcpClient();
        await ctrl.ConnectAsync(System.Net.IPAddress.Loopback, _port);
        var stream = ctrl.GetStream();
        var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
        var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        await reader.ReadLineAsync();
        await writer.WriteLineAsync("USER anonymous");
        await reader.ReadLineAsync();

        await writer.WriteLineAsync("PASV");
        var pasvResp = await reader.ReadLineAsync();
        var parts = pasvResp.Split('(', ')')[1].Split(',');
        int dataPort = int.Parse(parts[4]) * 256 + int.Parse(parts[5]);

        using var data = new System.Net.Sockets.TcpClient();
        await data.ConnectAsync(System.Net.IPAddress.Loopback, dataPort);

        await writer.WriteLineAsync("RETR test.txt");
        var retrResp = await reader.ReadLineAsync();
        retrResp.Should().StartWith("150");

        var dataReader = new System.IO.StreamReader(data.GetStream(), Encoding.UTF8);
        var content = await dataReader.ReadToEndAsync();
        data.Close();

        content.Should().Be("hello world");

        var finalResp = await reader.ReadLineAsync();
        finalResp.Should().StartWith("226");

        await writer.WriteLineAsync("QUIT");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dele_File_RemovesFromDisk()
    {
        using var ctrl = new System.Net.Sockets.TcpClient();
        await ctrl.ConnectAsync(System.Net.IPAddress.Loopback, _port);
        var stream = ctrl.GetStream();
        var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
        var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        await reader.ReadLineAsync();
        await writer.WriteLineAsync("USER anonymous");
        await reader.ReadLineAsync();

        await writer.WriteLineAsync("DELE test.txt");
        var deleResp = await reader.ReadLineAsync();
        deleResp.Should().StartWith("250");
        File.Exists(Path.Combine(_tempDir, "test.txt")).Should().BeFalse();

        await writer.WriteLineAsync("QUIT");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Mkd_CreatesDirectory()
    {
        using var ctrl = new System.Net.Sockets.TcpClient();
        await ctrl.ConnectAsync(System.Net.IPAddress.Loopback, _port);
        var stream = ctrl.GetStream();
        var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
        var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        await reader.ReadLineAsync();
        await writer.WriteLineAsync("USER anonymous");
        await reader.ReadLineAsync();

        await writer.WriteLineAsync("MKD testdir");
        var mkdResp = await reader.ReadLineAsync();
        mkdResp.Should().StartWith("257");
        Directory.Exists(Path.Combine(_tempDir, "testdir")).Should().BeTrue();

        await writer.WriteLineAsync("QUIT");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Rmd_RemovesDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "dirtodel"));

        using var ctrl = new System.Net.Sockets.TcpClient();
        await ctrl.ConnectAsync(System.Net.IPAddress.Loopback, _port);
        var stream = ctrl.GetStream();
        var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
        var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        await reader.ReadLineAsync();
        await writer.WriteLineAsync("USER anonymous");
        await reader.ReadLineAsync();

        await writer.WriteLineAsync("RMD dirtodel");
        var rmdResp = await reader.ReadLineAsync();
        rmdResp.Should().StartWith("250");
        Directory.Exists(Path.Combine(_tempDir, "dirtodel")).Should().BeFalse();

        await writer.WriteLineAsync("QUIT");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task List_ReturnsDirectoryListing()
    {
        using var ctrl = new System.Net.Sockets.TcpClient();
        await ctrl.ConnectAsync(System.Net.IPAddress.Loopback, _port);
        var stream = ctrl.GetStream();
        var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
        var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        await reader.ReadLineAsync();
        await writer.WriteLineAsync("USER anonymous");
        await reader.ReadLineAsync();

        await writer.WriteLineAsync("PASV");
        var pasvResp = await reader.ReadLineAsync();
        var parts = pasvResp.Split('(', ')')[1].Split(',');
        int dataPort = int.Parse(parts[4]) * 256 + int.Parse(parts[5]);

        using var data = new System.Net.Sockets.TcpClient();
        await data.ConnectAsync(System.Net.IPAddress.Loopback, dataPort);

        await writer.WriteLineAsync("LIST");
        var listResp = await reader.ReadLineAsync();
        listResp.Should().StartWith("150");

        var dataReader = new System.IO.StreamReader(data.GetStream(), Encoding.UTF8);
        var listing = await dataReader.ReadToEndAsync();
        data.Close();

        listing.Should().Contain("test.txt");

        var finalResp = await reader.ReadLineAsync();
        finalResp.Should().StartWith("226");

        await writer.WriteLineAsync("QUIT");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TypeI_SetsBinaryMode()
    {
        using var ctrl = new System.Net.Sockets.TcpClient();
        await ctrl.ConnectAsync(System.Net.IPAddress.Loopback, _port);
        var stream = ctrl.GetStream();
        var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
        var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        await reader.ReadLineAsync();
        await writer.WriteLineAsync("USER anonymous");
        await reader.ReadLineAsync();

        await writer.WriteLineAsync("TYPE I");
        var typeResp = await reader.ReadLineAsync();
        typeResp.Should().StartWith("200");

        await writer.WriteLineAsync("QUIT");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TwoClients_Simultaneous_BothConnect()
    {
        async Task<bool> ConnectAndPingAsync()
        {
            using var c = new System.Net.Sockets.TcpClient();
            await c.ConnectAsync(System.Net.IPAddress.Loopback, _port);
            var s = c.GetStream();
            var sr = new System.IO.StreamReader(s, Encoding.UTF8);
            _ = await sr.ReadLineAsync();
            var bytes = Encoding.UTF8.GetBytes("USER anonymous\r\n");
            await s.WriteAsync(bytes, 0, bytes.Length);
            var resp = await sr.ReadLineAsync();
            bytes = Encoding.UTF8.GetBytes("QUIT\r\n");
            await s.WriteAsync(bytes, 0, bytes.Length);
            return resp?.StartsWith("230") == true;
        }

        var results = await Task.WhenAll(ConnectAndPingAsync(), ConnectAndPingAsync());
        results.Should().AllBeEquivalentTo(true);
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

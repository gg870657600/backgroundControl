using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using backgroundControl.Tools;

public class HttpFileServerTests : IDisposable
{
    private readonly HttpFileServer _server;
    private readonly string _tempDir;
    private readonly int _port;

    public HttpFileServerTests()
    {
        _port = GetFreePort();
        _tempDir = Path.Combine(Path.GetTempPath(), "HttpFileServerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "hello world");

        var cfg = new HttpConfig
        {
            Port = _port,
            RootDir = _tempDir,
            Enabled = true
        };

        _server = new HttpFileServer(cfg);
        _server.OnLog += (msg) => { };
        _server.StartAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Root_ReturnsDirectoryListing()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var response = await client.GetAsync($"http://localhost:{_port}/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_ExistingFile_ReturnsContent()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var response = await client.GetAsync($"http://localhost:{_port}/test.txt");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("hello world");
    }

    [Fact]
    public async Task Get_NonexistentFile_Returns404()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var response = await client.GetAsync($"http://localhost:{_port}/nonexistent.txt");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
        _server.Stop();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Post_UploadFile_WritesToDisk()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var content = new MultipartFormDataContent
        {
            { new StringContent("uploaded content"), "file", "upload.txt" }
        };
        var response = await client.PostAsync($"http://localhost:{_port}/upload", content);

        var errorBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Redirect, $"Server returned 500 with body: {errorBody}");
        var uploaded = Path.Combine(_tempDir, "upload.txt");
        File.Exists(uploaded).Should().BeTrue();
        File.ReadAllText(uploaded).Should().Be("uploaded content");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Get_WithRange_ReturnsPartialContent()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{_port}/test.txt");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 4);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("hello");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Delete_ExistingFile_Returns200AndRemovesFile()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var response = await client.DeleteAsync($"http://localhost:{_port}/test.txt");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        File.Exists(Path.Combine(_tempDir, "test.txt")).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Delete_NonexistentFile_Returns404()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var response = await client.DeleteAsync($"http://localhost:{_port}/nope.txt");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Get_PathTraversal_Returns403()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _port);
        using var stream = tcp.GetStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n" };
        await writer.WriteLineAsync($"GET /../../../windows/win.ini HTTP/1.1");
        await writer.WriteLineAsync($"Host: localhost:{_port}");
        await writer.WriteLineAsync("Connection: close");
        await writer.WriteLineAsync();
        await writer.FlushAsync();

        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        var response = await reader.ReadToEndAsync();

        response.Should().Contain("403");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Post_LargeFile_UploadsCorrectly()
    {
        var largeContent = new string('A', 5 * 1024 * 1024);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var content = new MultipartFormDataContent
        {
            { new StringContent(largeContent), "file", "large.bin" }
        };
        var response = await client.PostAsync($"http://localhost:{_port}/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var uploaded = Path.Combine(_tempDir, "large.bin");
        File.Exists(uploaded).Should().BeTrue();
        new FileInfo(uploaded).Length.Should().Be(5 * 1024 * 1024);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Get_ConcurrentDownloads_AllSucceed()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            client.GetAsync($"http://localhost:{_port}/test.txt"));
        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
    }

    private static int _nextPort = 18800;
    private static int GetFreePort() => Interlocked.Increment(ref _nextPort);
}

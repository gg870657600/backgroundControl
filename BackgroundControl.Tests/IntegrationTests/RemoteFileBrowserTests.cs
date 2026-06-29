using System.Diagnostics;
using backgroundControl.Tools;
using Xunit.Abstractions;
using Xunit.Sdk;

public class RemoteFileBrowserTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IRemoteFileService _service = new WinSCPFileService();
    private readonly string _remoteBase;
    private readonly bool _ready;

    public RemoteFileBrowserTests(ITestOutputHelper output)
    {
        _output = output;
        var recent = SshHistoryManager.Load()
            .Where(e => e.ConnectionType == "SSH")
            .MaxBy(e => e.LastUsed);

        if (recent == null)
        {
            _output.WriteLine("无 SSH 历史记录，跳过测试");
            _ready = false;
            _remoteBase = null!;
            return;
        }

        var pass = SshHistoryManager.DecryptPassword(recent.Password);
        _output.WriteLine($"连接设备: {recent.Ip}:{recent.Port}");

        try
        {
            _ready = _service.Connect(recent.Ip, recent.Port, recent.Username, pass);
            if (!_ready)
            {
                _output.WriteLine("连接失败，跳过测试");
                _remoteBase = null!;
                return;
            }

            _remoteBase = "/mnt/common/opencode-test-" + Guid.NewGuid().ToString("N")[..8];
            _service.CreateDirectory(_remoteBase);
            _output.WriteLine($"测试目录: {_remoteBase}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"测试环境准备失败: {ex.Message}");
            _ready = false;
            _remoteBase = null!;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task 上传单个文件_远程端存在()
    {
        if (!_ready) throw new SkipTestException("设备未连接");
 
        var local = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(local, "hello winscp upload"u8.ToArray());
            var remote = _remoteBase + "/test-upload.txt";
            _service.UploadFile(local, remote);

            var files = _service.ListDirectory(_remoteBase);
            files.Should().Contain(f => f.Name == "test-upload.txt");
        }
        finally { File.Delete(local); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task 上传多个文件_全部存在()
    {
        if (!_ready) throw new SkipTestException("设备未连接");

        var names = new[] { "multi-a.txt", "multi-b.txt", "multi-c.txt" };
        var locals = names.Select(_ => Path.GetTempFileName()).ToArray();
        try
        {
            for (int i = 0; i < names.Length; i++)
                await File.WriteAllBytesAsync(locals[i], Encoding.UTF8.GetBytes($"content {i}"));

            foreach (var (local, name) in locals.Zip(names))
                _service.UploadFile(local, _remoteBase + "/" + name);

            var files = _service.ListDirectory(_remoteBase);
            foreach (var name in names)
                files.Should().Contain(f => f.Name == name, $"{name} should exist after upload");
        }
        finally { foreach (var f in locals) File.Delete(f); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task 下载单个文件_内容一致()
    {
        if (!_ready) throw new SkipTestException("设备未连接");

        var localUpload = Path.GetTempFileName();
        var localDownload = Path.GetTempFileName();
        try
        {
            var original = "download verification content"u8.ToArray();
            await File.WriteAllBytesAsync(localUpload, original);
            var remote = _remoteBase + "/test-download.txt";
            _service.UploadFile(localUpload, remote);

            _service.DownloadFile(remote, localDownload);
            var downloaded = await File.ReadAllBytesAsync(localDownload);
            downloaded.Should().BeEquivalentTo(original);
        }
        finally { File.Delete(localUpload); File.Delete(localDownload); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task 下载多个文件_内容均匹配()
    {
        if (!_ready) throw new SkipTestException("设备未连接");

        var contents = new Dictionary<string, byte[]>
        {
            ["dl-a.txt"] = "alpha data"u8.ToArray(),
            ["dl-b.txt"] = "beta data"u8.ToArray(),
        };

        try
        {
            foreach (var (name, data) in contents)
            {
                var local = Path.GetTempFileName();
                await File.WriteAllBytesAsync(local, data);
                _service.UploadFile(local, _remoteBase + "/" + name);
                File.Delete(local);
            }

            foreach (var (name, original) in contents)
            {
                var dest = Path.GetTempFileName();
                _service.DownloadFile(_remoteBase + "/" + name, dest);
                var downloaded = await File.ReadAllBytesAsync(dest);
                downloaded.Should().BeEquivalentTo(original, $"{name} content should match");
                File.Delete(dest);
            }
        }
        finally { /* cleanup */ }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task 删除文件_文件不再存在()
    {
        if (!_ready) throw new SkipTestException("设备未连接");

        var local = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(local, "to be deleted"u8.ToArray());
            var remote = _remoteBase + "/test-delete.txt";
            _service.UploadFile(local, remote);

            var files = _service.ListDirectory(_remoteBase);
            files.Should().Contain(f => f.Name == "test-delete.txt");

            _service.RemoveFiles(remote);

            files = _service.ListDirectory(_remoteBase);
            files.Should().NotContain(f => f.Name == "test-delete.txt");
        }
        finally { File.Delete(local); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task 下载不存在的文件_抛出异常()
    {
        if (!_ready) throw new SkipTestException("设备未连接");

        var remotePath = _remoteBase + "/missing-" + Guid.NewGuid() + ".txt";
        var dest = Path.GetTempFileName();
        try
        {
            Action act = () => _service.DownloadFile(remotePath, dest);
            act.Should().Throw<Exception>();
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task 上传到不存在的目录_抛出异常()
    {
        if (!_ready) throw new SkipTestException("设备未连接");

        var local = Path.GetTempFileName();
        try
        {
            Action act = () => _service.UploadFile(local, _remoteBase + "/nosuchdir/file.txt");
            act.Should().Throw<Exception>();
        }
        finally { File.Delete(local); }
    }

    public void Dispose()
    {
        if (_ready)
        {
            try
            {
                foreach (var f in _service.ListDirectory(_remoteBase))
                {
                    if (f.Name is "." or "..") continue;
                    try { _service.RemoveFiles(f.FullPath); } catch { }
                }
                _service.RemoveFiles(_remoteBase);
            }
            catch { }
            _service.Dispose();
        }
    }
}

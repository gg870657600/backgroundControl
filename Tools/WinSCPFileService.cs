using System.Diagnostics;
using WinSCP;

namespace backgroundControl.Tools;

public class WinSCPFileService : IRemoteFileService
{
    private Session? _session;
    private readonly TransferOptions _transferOptions = new() { TransferMode = TransferMode.Binary };

    public bool IsConnected => _session is { Opened: true };

    public bool Connect(string host, int port, string user, string pass)
    {
        Disconnect();

        var opts = new SessionOptions
        {
            Protocol = Protocol.Sftp,
            HostName = host,
            PortNumber = port,
            UserName = user,
            Password = pass,
        };
        opts.AddRawSettings("Utf", "1");
        opts.AddRawSettings("FSProtocol", "1");

        using (var tmp = new Session())
        {
            try { opts.SshHostKeyFingerprint = tmp.ScanFingerprint(opts, "SHA-256"); }
            catch { }
        }

        try
        {
            _session = new Session();
            _session.Open(opts);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WinSCP 连接失败: {ex.Message}");
            _session?.Dispose();
            _session = null;
            return false;
        }
    }

    public void Disconnect()
    {
        if (_session is { Opened: true })
        {
            _session.Close();
        }
        _session?.Dispose();
        _session = null;
    }

    public List<RemoteFileInfo> ListDirectory(string path)
    {
        EnsureConnected();
        var dir = _session!.ListDirectory(path);
        return dir.Files
            .Where(f => f.Name != "." && f.Name != "..")
            .Select(f => new RemoteFileInfo
            {
                Name = f.IsDirectory ? f.Name + "/" : f.Name,
                Size = f.IsDirectory ? "-" : f.Length < 1024 ? $"{f.Length} B" : $"{f.Length / 1024} KB",
                ModifyTime = f.LastWriteTime,
                FullPath = f.FullName,
                IsDirectory = f.IsDirectory,
            })
            .ToList();
    }

    public void UploadFile(string localPath, string remotePath)
    {
        EnsureConnected();
        var result = _session!.PutFiles(localPath, remotePath, false, _transferOptions);
        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"上传失败: {string.Join("; ", result.Failures.Select(f => f.Message))}");
    }

    public void DownloadFile(string remotePath, string localPath)
    {
        EnsureConnected();
        var result = _session!.GetFiles(remotePath, localPath, false, _transferOptions);
        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"下载失败: {string.Join("; ", result.Failures.Select(f => f.Message))}");
    }

    public void RemoveFiles(string path)
    {
        EnsureConnected();
        _session!.RemoveFiles(path);
    }

    public void CreateDirectory(string path)
    {
        EnsureConnected();
        _session!.CreateDirectory(path);
    }

    public void DeleteDirectory(string path)
    {
        EnsureConnected();
        _session!.RemoveFiles(path);
    }

    public void Dispose()
    {
        Disconnect();
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to remote server");
    }
}

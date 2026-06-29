using backgroundControl;

namespace backgroundControl.Tools;

public interface IRemoteFileService : IDisposable
{
    bool Connect(string host, int port, string user, string pass);
    void Disconnect();
    bool IsConnected { get; }
    List<RemoteFileInfo> ListDirectory(string path);
    void UploadFile(string localPath, string remotePath);
    void DownloadFile(string remotePath, string localPath);
    void RemoveFiles(string path);
    void CreateDirectory(string path);
    void DeleteDirectory(string path);
}

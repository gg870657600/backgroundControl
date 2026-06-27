using Renci.SshNet;

namespace backgroundControl.Tools;

public interface IShellStream
{
    bool CanWrite { get; }
    void Write(string text);
    Task<int> ReadAsync(byte[] buffer, int offset, int count);
}

public class ShellStreamAdapter : IShellStream
{
    private readonly ShellStream _inner;
    public ShellStreamAdapter(ShellStream inner) => _inner = inner;
    public bool CanWrite => _inner.CanWrite;
    public void Write(string text) => _inner.Write(text);
    public Task<int> ReadAsync(byte[] buffer, int offset, int count) => _inner.ReadAsync(buffer, offset, count);
}

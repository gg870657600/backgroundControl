public class TempFileFixture : IDisposable
{
    public string TempDir { get; }

    public TempFileFixture()
    {
        TempDir = Path.Combine(Path.GetTempPath(), "BackgroundControlTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDir);
    }

    public string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(TempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(TempDir, recursive: true); } catch { }
    }
}

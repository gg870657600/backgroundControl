using backgroundControl.Tools;

public class SshHistoryTests
{
    [Fact]
    public void Load_FileNotExists_ReturnsEmpty()
    {
        var result = SshHistoryManager.Load("nonexistent/path.json");

        result.Should().BeEmpty();
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var entries = new List<SshHistoryEntry>
            {
                new SshHistoryEntry
                {
                    Ip = "192.168.1.1",
                    Port = 22,
                    Username = "admin",
                    Password = "dGVzdA==",
                    ConnectionType = "SSH",
                    LastUsed = DateTime.UtcNow
                }
            };

            SshHistoryManager.Save(entries, path);
            var loaded = SshHistoryManager.Load(path);

            loaded.Should().HaveCount(1);
            loaded[0].Ip.Should().Be("192.168.1.1");
            loaded[0].Username.Should().Be("admin");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "corrupted json {{{");
            var result = SshHistoryManager.Load(path);

            result.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

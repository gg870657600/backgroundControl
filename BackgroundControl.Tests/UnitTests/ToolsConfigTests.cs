using backgroundControl.Tools;

public class ToolsConfigTests
{
    [Fact]
    public void Load_FileNotExists_ReturnsDefaults()
    {
        var cfg = ToolsConfig.Load("nonexistent/path.json");

        cfg.Should().NotBeNull();
        cfg.Http.Port.Should().Be(8080);
        cfg.Ftp.Port.Should().Be(2121);
        cfg.Iperf.DefaultPort.Should().Be(5201);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesValues()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cfg = new ToolsConfig();
            cfg.Http.Port = 9090;
            cfg.Ftp.Port = 2122;
            cfg.Save(path);

            var loaded = ToolsConfig.Load(path);
            loaded.Http.Port.Should().Be(9090);
            loaded.Ftp.Port.Should().Be(2122);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "not json at all {{{");
            var cfg = ToolsConfig.Load(path);

            cfg.Should().NotBeNull();
            cfg.Http.Port.Should().Be(8080);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

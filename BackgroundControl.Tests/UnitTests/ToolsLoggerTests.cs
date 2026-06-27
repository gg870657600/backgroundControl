using backgroundControl.Tools;

public class ToolsLoggerTests
{
    [Fact]
    public void Info_WritesFormattedEntry()
    {
        ToolsLogger.Info(ToolsLogger.Source.Http, "test msg");
        var snapshot = ToolsLogger.Snapshot();
        snapshot.Should().Contain(l => l.Text.Contains("[INFO ]") && l.Text.Contains("test msg"));
    }

    [Fact]
    public void Warn_WritesFormattedEntry()
    {
        ToolsLogger.Warn(ToolsLogger.Source.Ftp, "warn msg");
        var snapshot = ToolsLogger.Snapshot();
        snapshot.Should().Contain(l => l.Text.Contains("[WARN ]") && l.Text.Contains("warn msg"));
    }

    [Fact]
    public void Error_WritesFormattedEntry()
    {
        ToolsLogger.Error(ToolsLogger.Source.Iperf, "err msg");
        var snapshot = ToolsLogger.Snapshot();
        snapshot.Should().Contain(l => l.Text.Contains("[ERROR]") && l.Text.Contains("err msg"));
    }

    [Fact]
    public void Debug_WritesFormattedEntry()
    {
        ToolsLogger.Debug(ToolsLogger.Source.System, "dbg msg");
        var snapshot = ToolsLogger.Snapshot();
        snapshot.Should().Contain(l => l.Text.Contains("[DEBUG]") && l.Text.Contains("dbg msg"));
    }

    [Fact]
    public void LogAppended_EventFiresOnEachWrite()
    {
        ToolsLogger.Info(ToolsLogger.Source.Http, "evt_a");
        ToolsLogger.Warn(ToolsLogger.Source.Ftp, "evt_b");
        var snapshot = ToolsLogger.Snapshot();
        snapshot.Count(l => l.Text.Contains("evt_a") || l.Text.Contains("evt_b")).Should().Be(2);
    }

    [Fact]
    public void Snapshot_ReturnsAllEntries()
    {
        ToolsLogger.Info(ToolsLogger.Source.Http, "x");
        ToolsLogger.Info(ToolsLogger.Source.Ftp, "y");
        ToolsLogger.Info(ToolsLogger.Source.Iperf, "z");
        var snapshot = ToolsLogger.Snapshot();
        snapshot.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void CircularBuffer_TrimsAt500()
    {
        for (int i = 0; i < 501; i++)
            ToolsLogger.Info(ToolsLogger.Source.System, $"line {i}");

        var snapshot = ToolsLogger.Snapshot();
        snapshot.Should().HaveCount(500);
    }

    [Fact]
    public void LogLine_HasTimestampAndSource()
    {
        ToolsLogger.Info(ToolsLogger.Source.Http, "ts test");
        var snapshot = ToolsLogger.Snapshot();
        var line = snapshot.Last(l => l.Text.Contains("ts test"));
        line.Time.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(5));
        line.Source.Should().Be(ToolsLogger.Source.Http);
    }
}

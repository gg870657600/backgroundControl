using backgroundControl.Tools;

public class HighlightRuleTests
{
    [Fact]
    public void CompileRules_ValidPattern_ReturnsCompiledRegex()
    {
        var rules = new List<HighlightRule>
        {
            new HighlightRule { Pattern = "error", Color = "Red" },
        };
        var compiled = HighlightTerminalConnection.CompileRules(rules);
        compiled.Should().HaveCount(1);
        compiled[0].Item1.IsMatch("error").Should().BeTrue();
        compiled[0].Item1.IsMatch("ERROR").Should().BeTrue();
        compiled[0].Item2.Should().Be("Red");
    }

    [Fact]
    public void CompileRules_InvalidPattern_Skipped()
    {
        var rules = new List<HighlightRule>
        {
            new HighlightRule { Pattern = "[invalid", Color = "Red" },
        };
        var compiled = HighlightTerminalConnection.CompileRules(rules);
        compiled.Should().BeEmpty();
    }

    [Fact]
    public void CompileRules_MultipleValid_AllCompiled()
    {
        var rules = new List<HighlightRule>
        {
            new HighlightRule { Pattern = "err", Color = "Red" },
            new HighlightRule { Pattern = "ok", Color = "Green" },
            new HighlightRule { Pattern = "warn", Color = "Yellow" },
        };
        var compiled = HighlightTerminalConnection.CompileRules(rules);
        compiled.Should().HaveCount(3);
    }

    [Fact]
    public void LoadRules_FileNotExists_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var loaded = HighlightTerminalConnection.LoadRules(path);
        loaded.Should().BeEmpty();
    }

    [Fact]
    public void LoadRules_ValidJson_Deserializes()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, @"[{""Pattern"":""err"",""Color"":""Red""}]");
        var loaded = HighlightTerminalConnection.LoadRules(path);
        loaded.Should().HaveCount(1);
        loaded[0].Pattern.Should().Be("err");
        loaded[0].Color.Should().Be("Red");
    }

    [Fact]
    public void LoadRules_CorruptedJson_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, @"{{broken");
        var loaded = HighlightTerminalConnection.LoadRules(path);
        loaded.Should().BeEmpty();
    }

    [Fact]
    public void LoadRules_CaseInsensitiveProperty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, @"[{""pattern"":""err"",""color"":""Blue""}]");
        var loaded = HighlightTerminalConnection.LoadRules(path);
        loaded.Should().HaveCount(1);
        loaded[0].Pattern.Should().Be("err");
        loaded[0].Color.Should().Be("Blue");
    }
}

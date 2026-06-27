using System.Reflection;

using backgroundControl;

public class RuleStorageTests
{
    private static readonly string TestDir = Path.Combine(Path.GetTempPath(), "RuleStorageTests_" + Guid.NewGuid().ToString("N"));
    private static readonly string TestFile = Path.Combine(TestDir, "rules.json");

    public RuleStorageTests()
    {
        Directory.CreateDirectory(TestDir);
        Environment.CurrentDirectory = TestDir;
        // Reset static state by clearing any existing rules file
        if (File.Exists(TestFile)) File.Delete(TestFile);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var rules = new List<RuleItem>
        {
            new RuleItem { Keywords = "测试 关键词", Command = "get-test" },
            new RuleItem { Keywords = "版本 ver", Command = "ver" },
        };
        RuleStorage.SaveRules(rules);
        var loaded = RuleStorage.LoadRules();
        loaded.Should().HaveCount(2);
        loaded[0].Command.Should().Be("get-test");
        loaded[0].Keywords.Should().Be("测试 关键词");
    }

    [Fact]
    public void Load_FileNotExists_ReturnsDefaultRules()
    {
        if (File.Exists(TestFile)) File.Delete(TestFile);
        var rules = RuleStorage.LoadRules();
        rules.Should().NotBeNull();
        rules.Should().HaveCountGreaterThanOrEqualTo(30);
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsDefaultRules()
    {
        File.WriteAllText(TestFile, "{invalid json!!!");
        var rules = RuleStorage.LoadRules();
        rules.Should().NotBeNull();
        rules.Should().HaveCountGreaterThanOrEqualTo(30);
    }

    [Fact]
    public void DefaultRules_HaveAtLeast30Entries()
    {
        if (File.Exists(TestFile)) File.Delete(TestFile);
        var rules = RuleStorage.LoadRules();
        rules.Should().HaveCountGreaterThanOrEqualTo(30);
    }

    [Fact]
    public void DefaultRules_EachHasNonEmptyCommand()
    {
        if (File.Exists(TestFile)) File.Delete(TestFile);
        var rules = RuleStorage.LoadRules();
        rules.Should().AllSatisfy(r => r.Command.Should().NotBeNullOrEmpty());
    }
}

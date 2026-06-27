using backgroundControl;

public class CommandClassificationTests
{
    [Theory]
    [InlineData("ls", true)]
    [InlineData("cd /tmp", true)]
    [InlineData("grep foo bar", true)]
    [InlineData("get-ne-type", false)]
    [InlineData("set-rf:on", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLinuxCommand_ReturnsExpected(string? input, bool expected)
    {
        CommandClassifier.IsLinuxCommand(input!).Should().Be(expected);
    }

    [Theory]
    [InlineData("get-ne-type", true)]
    [InlineData("set-rf:on", true)]
    [InlineData("dump-config", true)]
    [InlineData("calibre-status", true)]
    [InlineData("ls", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDirectCommand_ReturnsExpected(string? input, bool expected)
    {
        CommandClassifier.IsDirectCommand(input!).Should().Be(expected);
    }

    [Fact]
    public void ClassifyCommand_LinuxCommand_ReturnsIsLinuxTrue()
    {
        var patterns = new List<CommandPattern>();
        var rules = new List<IntentRule>();

        var (isLinux, _) = CommandClassifier.ClassifyCommand("ls -la", patterns, rules);

        isLinux.Should().BeTrue();
    }

    [Fact]
    public void ClassifyCommand_DirectCommand_ReturnsIsLinuxFalse()
    {
        var patterns = new List<CommandPattern>();
        var rules = new List<IntentRule>();

        var (isLinux, finalCmd) = CommandClassifier.ClassifyCommand("get-ne-type", patterns, rules);

        isLinux.Should().BeFalse();
        finalCmd.Should().Be("get-ne-type");
    }

    [Fact]
    public void ClassifyCommand_RuleMatch_ReturnsMappedCommand()
    {
        var rules = new List<IntentRule>
        {
            new IntentRule("关站 关电源 关闭发射", "set-rf:off")
        };

        var (isLinux, finalCmd) = CommandClassifier.ClassifyCommand("关站", new List<CommandPattern>(), rules);

        isLinux.Should().BeFalse();
        finalCmd.Should().Be("set-rf:off");
    }

    [Fact]
    public void ClassifyCommand_UnknownCommand_ReturnsIsLinuxFalseForGetPrefix()
    {
        var (isLinux, finalCmd) = CommandClassifier.ClassifyCommand("get-foo", new List<CommandPattern>(), new List<IntentRule>());

        isLinux.Should().BeFalse();
        finalCmd.Should().Be("get-foo");
    }

    [Fact]
    public void ClassifyCommand_UnknownCommand_ReturnsIsLinuxTrueForNonDirect()
    {
        var (isLinux, finalCmd) = CommandClassifier.ClassifyCommand("foobar", new List<CommandPattern>(), new List<IntentRule>());

        isLinux.Should().BeTrue();
        finalCmd.Should().Be("foobar");
    }
}

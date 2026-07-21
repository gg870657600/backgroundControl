using backgroundControl;

public class CommandClassificationTests
{
    [Theory]
    [InlineData("/usr/bin/ls", true)]
    [InlineData("./script.sh", true)]
    [InlineData("ls", false)]
    [InlineData("cd /tmp", false)]
    [InlineData("grep foo bar", false)]
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

    public static IEnumerable<object[]> GetSwitchDecisionData =>
        new List<object[]>
        {
            // cmd            inTelnet  needsSwitch  toTelnet  finalCmd
            new object[] { "ls",          false, false,     false,    "ls"           },
            new object[] { "ls",          true,  true,      false,    "ls"           },
            new object[] { "get-ne-type", false, true,      true,     "get-ne-type"  },
            new object[] { "get-ne-type", true,  false,     true,     "get-ne-type"  },
            new object[] { "foobar",      false, false,     false,    "foobar"       },
            new object[] { "foobar",      true,  true,      false,    "foobar"       },
        };

    [Theory]
    [MemberData(nameof(GetSwitchDecisionData))]
    public void 切换决策矩阵_六种组合全部正确(string cmd, bool inTelnet, bool expectedNeedsSwitch, bool expectedToTelnet, string expectedFinalCmd)
    {
        var patterns = new List<CommandPattern>();
        var rules = new List<IntentRule>();

        var (needsSwitch, toTelnet, finalCmd) = CommandClassifier.GetSwitchDecision(cmd, patterns, rules, inTelnet);

        needsSwitch.Should().Be(expectedNeedsSwitch);
        toTelnet.Should().Be(expectedToTelnet);
        finalCmd.Should().Be(expectedFinalCmd);
    }

    [Fact]
    public void 规则匹配的命令_SSH环境_需切Telnet并返回映射值()
    {
        var rules = new List<IntentRule>
        {
            new IntentRule("关站 关电源", "set-rf:off")
        };

        var (needsSwitch, toTelnet, finalCmd) = CommandClassifier.GetSwitchDecision(
            "关站", new List<CommandPattern>(), rules, inTelnet: false);

        needsSwitch.Should().BeTrue();
        toTelnet.Should().BeTrue();
        finalCmd.Should().Be("set-rf:off");
    }

    [Fact]
    public void 规则匹配的命令_已在Telnet_无需切换()
    {
        var rules = new List<IntentRule>
        {
            new IntentRule("关站 关电源", "set-rf:off")
        };

        var (needsSwitch, toTelnet, finalCmd) = CommandClassifier.GetSwitchDecision(
            "关站", new List<CommandPattern>(), rules, inTelnet: true);

        needsSwitch.Should().BeFalse();
        toTelnet.Should().BeTrue();
        finalCmd.Should().Be("set-rf:off");
    }
}

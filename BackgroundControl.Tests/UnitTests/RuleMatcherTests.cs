using backgroundControl;

public class RuleMatcherTests
{
    [Fact]
    public void MatchCommand_ExactMatch_ReturnsRuleCommand()
    {
        var rules = new List<IntentRule>
        {
            new IntentRule("关站 关电源", "set-rf:off")
        };

        var result = CommandClassifier.MatchCommand("关站", new List<CommandPattern>(), rules);

        result.Should().Be("set-rf:off");
    }

    [Fact]
    public void MatchCommand_ContainsMatch_ReturnsRuleCommand()
    {
        var rules = new List<IntentRule>
        {
            new IntentRule("关站 关电源", "set-rf:off")
        };

        var result = CommandClassifier.MatchCommand("我想关站", new List<CommandPattern>(), rules);

        result.Should().Be("set-rf:off");
    }

    [Fact]
    public void MatchCommand_NoMatch_ReturnsUnknown()
    {
        var rules = new List<IntentRule>
        {
            new IntentRule("关站", "set-rf:off")
        };

        var result = CommandClassifier.MatchCommand("开站", new List<CommandPattern>(), rules);

        result.Should().Be("UNKNOWN");
    }

    [Fact]
    public void MatchCommand_BestScoreWins_OverTotalScore()
    {
        var rules = new List<IntentRule>
        {
            new IntentRule("关站", "set-rf:off"),
            new IntentRule("关站 关电源", "set-rf:power-off")
        };

        var result = CommandClassifier.MatchCommand("关电源", new List<CommandPattern>(), rules);

        result.Should().Be("set-rf:power-off");
    }

    [Fact]
    public void MatchCommand_TieBreakByTotalScore()
    {
        var rules = new List<IntentRule>
        {
            new IntentRule("关aupc scpc", "close aupc scpc"),
            new IntentRule("关站 aupc", "set-aupc:off")
        };

        var result = CommandClassifier.MatchCommand("关aupc scpc", new List<CommandPattern>(), rules);

        result.Should().Be("close aupc scpc");
    }

    [Fact]
    public void MatchCommand_DirectCommand_ReturnsDirect()
    {
        var result = CommandClassifier.MatchCommand("get-ne-type", new List<CommandPattern>(), new List<IntentRule>());

        result.Should().Be("get-ne-type");
    }
}

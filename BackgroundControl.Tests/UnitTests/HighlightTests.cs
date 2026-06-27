using backgroundControl.Tools;

public class HighlightTests
{
    [Fact]
    public void HighlightPattern_Match_WrapsWithColorCode()
    {
        var patterns = new[]
        {
            (new Regex("error", RegexOptions.IgnoreCase), "Red"),
        };

        var result = HighlightTerminalConnection.ApplyHighlight("test error test", patterns);

        result.Should().Be("test \x1b[Redmerror\x1b[0m test");
    }

    [Fact]
    public void HighlightPattern_NoMatch_OriginalString()
    {
        var patterns = new[]
        {
            (new Regex("error", RegexOptions.IgnoreCase), "Red"),
        };

        var result = HighlightTerminalConnection.ApplyHighlight("test ok test", patterns);

        result.Should().Be("test ok test");
    }

    [Fact]
    public void HighlightPattern_MultiplePatterns_AllColored()
    {
        var patterns = new[]
        {
            (new Regex("error", RegexOptions.IgnoreCase), "Red"),
            (new Regex("ok", RegexOptions.IgnoreCase), "Green"),
        };

        var result = HighlightTerminalConnection.ApplyHighlight("error then ok", patterns);

        result.Should().Contain("\x1b[Redm");
        result.Should().Contain("\x1b[Greenm");
    }

    [Fact]
    public void HighlightPattern_EmptyInput_EmptyOutput()
    {
        var result = HighlightTerminalConnection.ApplyHighlight("", Array.Empty<(Regex, string)>());

        result.Should().Be("");
    }

    [Fact]
    public void HighlightPattern_HtmlEntityPreserved()
    {
        var patterns = new[]
        {
            (new Regex("error", RegexOptions.IgnoreCase), "Red"),
        };

        var result = HighlightTerminalConnection.ApplyHighlight("error &amp; stuff", patterns);

        result.Should().Contain("&amp;");
    }
}

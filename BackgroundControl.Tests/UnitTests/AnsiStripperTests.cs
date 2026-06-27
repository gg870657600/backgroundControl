using backgroundControl;

public class AnsiStripperTests
{
    [Fact]
    public void StripAnsi_RemovesEscapeSequence()
    {
        var result = AnsiStripper.Strip("hello \x1b[31mworld\x1b[0m");
        result.Should().Be("hello world");
    }

    [Fact]
    public void StripAnsi_RemovesOSCSequence()
    {
        var result = AnsiStripper.Strip("\x1b]0;title\x07text");
        result.Should().Be("text");
    }

    [Fact]
    public void StripAnsi_RemovesControlChars()
    {
        var result = AnsiStripper.Strip("a\x01b\x02c\x1Fd");
        result.Should().NotContain("\x01").And.NotContain("\x02").And.NotContain("\x1F");
    }

    [Fact]
    public void StripAnsi_NormalizesNewlines()
    {
        var result = AnsiStripper.Strip("\r\nline\r\n");
        result.Should().Be("\nline\n");
    }

    [Fact]
    public void StripAnsi_EmptyInput_ReturnsEmpty()
    {
        AnsiStripper.Strip("").Should().Be("");
    }

    [Fact]
    public void StripAnsi_NullInput_ReturnsEmpty()
    {
        AnsiStripper.Strip(null!).Should().BeNull();
    }

    [Fact]
    public void StripAnsi_NoAnsi_ReturnsOriginal()
    {
        var result = AnsiStripper.Strip("plain text without ansi");
        result.Should().Be("plain text without ansi");
    }
}

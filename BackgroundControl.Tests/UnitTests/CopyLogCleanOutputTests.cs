using backgroundControl;

public class CopyLogCleanOutputTests
{
    [Fact]
    public void StripAnsi_MixedContent_ProducesCleanText()
    {
        string raw = "\x1b[31mError\x1b[0m: \x1b[1mdisk full\x1b[0m\x00";
        string clean = AnsiStripper.Strip(raw);
        clean.Should().Be("Error: disk full");
    }

    [Fact]
    public void StripAnsi_MultipleLines_PreservesNewlines()
    {
        string raw = "line1\r\nline2\nline3\rline4";
        string clean = AnsiStripper.Strip(raw);
        clean.Should().Be("line1\nline2\nline3line4");
    }

    [Fact]
    public void StripAnsi_ControlChars_Removed()
    {
        string raw = "a\x01b\x02c\x1Fd";
        string clean = AnsiStripper.Strip(raw);
        clean.Should().NotContain("\x01").And.NotContain("\x02").And.NotContain("\x1F");
    }

    [Fact]
    public void StripAnsi_RealWorldTerminalOutput()
    {
        string raw = "\r\n\x1b[?25ladmin@device:~$ \x1b[?25h\x1b[?25lget-ne-type\r\n\x1b[?25h\x1b[?25lE600X\r\n";
        string clean = AnsiStripper.Strip(raw);
        clean.Should().Be("\nadmin@device:~$ get-ne-type\nE600X\n");
    }
}

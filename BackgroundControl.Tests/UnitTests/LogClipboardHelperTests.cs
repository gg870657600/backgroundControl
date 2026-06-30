using System.Text;
using backgroundControl.Tools;

public class LogClipboardHelperTests
{
    [Fact]
    public void 空日志返回空()
    {
        var (text, isEmpty) = LogClipboardHelper.PrepareLog(new StringBuilder());

        isEmpty.Should().BeTrue();
        text.Should().BeEmpty();
    }

    [Fact]
    public void 空白日志返回空()
    {
        var (text, isEmpty) = LogClipboardHelper.PrepareLog(new StringBuilder("   \t  "));

        isEmpty.Should().BeTrue();
    }

    [Fact]
    public void 纯ASCII_字符数正确()
    {
        var sb = new StringBuilder("hello world");
        var (text, isEmpty) = LogClipboardHelper.PrepareLog(sb);

        isEmpty.Should().BeFalse();
        text.Should().Be("hello world");
        text.Length.Should().Be(11);
    }

    [Fact]
    public void 含中文_字符数正确()
    {
        var sb = new StringBuilder("日志复制成功");
        var (text, isEmpty) = LogClipboardHelper.PrepareLog(sb);

        isEmpty.Should().BeFalse();
        text.Should().Be("日志复制成功");
        text.Length.Should().Be(6);
    }

    [Fact]
    public void 含换行符_n替换为rn_长度增加()
    {
        var sb = new StringBuilder("line1\nline2\nline3");
        var (text, isEmpty) = LogClipboardHelper.PrepareLog(sb);

        isEmpty.Should().BeFalse();
        text.Should().Be("line1\r\nline2\r\nline3");
        text.Length.Should().Be(19); // 原始 17 字符 + 2 个 \r\n (每个比 \n 多 1 字符)
    }

    [Fact]
    public void 混合换行格式_统一为rn()
    {
        // _cleanOutput 中所有换行已是 \n（由 AnsiStripper 标准化）
        var sb = new StringBuilder("a\nb\nc\n");
        var (text, _) = LogClipboardHelper.PrepareLog(sb);

        text.Should().Be("a\r\nb\r\nc\r\n");
    }

    [Fact]
    public void 大量行_字符数累加正确()
    {
        var sb = new StringBuilder();
        int lineCount = 1000;
        for (int i = 0; i < lineCount; i++)
            sb.Append($"line-{i:D4}\n");

        var (text, isEmpty) = LogClipboardHelper.PrepareLog(sb);

        isEmpty.Should().BeFalse();
        text.Should().Contain("line-0000");
        text.Should().Contain("line-0999");
        // 每行 "line-XXXX\n" = 10 字符，替换后 "line-XXXX\r\n" = 11 字符
        text.Length.Should().Be(lineCount * 11);
    }

    [Fact]
    public void 字符数与MessageBox显示一致()
    {
        // ✅ (U+2705) 在 BMP 内，string.Length = 1
        var sb = new StringBuilder("✅ 已复制 42 字符");
        var (text, _) = LogClipboardHelper.PrepareLog(sb);

        int displayedCount = text.Length;
        displayedCount.Should().Be(11);
    }
}

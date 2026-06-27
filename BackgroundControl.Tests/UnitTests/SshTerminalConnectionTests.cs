using backgroundControl.Tools;
using backgroundControl;

public class SshTerminalConnectionTests
{
    private class MockShellStream : IShellStream
    {
        public bool CanWrite => true;
        public string WrittenText = "";
        public void Write(string text) => WrittenText += text;
        public Task<int> ReadAsync(byte[] buffer, int offset, int count) => Task.FromResult(0);
    }

    [Fact]
    public void WriteInput_EnterWithBuffer_CallsHandler()
    {
        var mock = new MockShellStream();
        string? capturedCmd = null;
        var conn = new SshTerminalConnection(mock, cmd => capturedCmd = cmd);

        conn.WriteInput("ls\r");

        capturedCmd.Should().Be("ls");
        conn.HasPendingInput.Should().BeFalse();
    }

    [Fact]
    public void WriteInput_Backspace_RemovesLastChar()
    {
        var mock = new MockShellStream();
        var conn = new SshTerminalConnection(mock);

        conn.WriteInput("ab\b");

        conn.HasPendingInput.Should().BeTrue();
        // After "ab\b", buffer should contain "a"
        // Verify by submitting with \r
        string? capturedCmd = null;
        conn = new SshTerminalConnection(mock, cmd => capturedCmd = cmd);
        conn.WriteInput("ab\b\r");
        capturedCmd.Should().Be("a");
    }

    [Fact]
    public void WriteInput_Backspace_EmptyBufferNoOp()
    {
        var mock = new MockShellStream();
        string? capturedCmd = null;
        var conn = new SshTerminalConnection(mock, cmd => capturedCmd = cmd);

        conn.WriteInput("\b\r");

        capturedCmd.Should().BeNull();
    }

    [Fact]
    public void WriteInput_PrintableChars_AppendToBuffer()
    {
        var mock = new MockShellStream();
        var conn = new SshTerminalConnection(mock);

        conn.WriteInput("hello");

        conn.HasPendingInput.Should().BeTrue();
    }

    [Fact]
    public void WriteInput_Newline_Skipped()
    {
        var mock = new MockShellStream();
        string? capturedCmd = null;
        var conn = new SshTerminalConnection(mock, cmd => capturedCmd = cmd);

        conn.WriteInput("ab\ncd\r");

        capturedCmd.Should().Be("abcd");
    }

    [Fact]
    public void WriteInput_ControlChar_PassedToStream()
    {
        var mock = new MockShellStream();
        var conn = new SshTerminalConnection(mock);

        conn.WriteInput("\x1b[A");

        mock.WrittenText.Should().Contain("\x1b[A");
    }

    [Fact]
    public void HasPendingInput_InitiallyFalse()
    {
        var conn = new SshTerminalConnection(new MockShellStream());
        conn.HasPendingInput.Should().BeFalse();
    }

    [Fact]
    public void WriteInput_EnterWithEmptyBuffer_DoesNotCallHandler()
    {
        var mock = new MockShellStream();
        string? capturedCmd = null;
        var conn = new SshTerminalConnection(mock, cmd => capturedCmd = cmd);

        conn.WriteInput("\r");

        capturedCmd.Should().BeNull();
    }
}

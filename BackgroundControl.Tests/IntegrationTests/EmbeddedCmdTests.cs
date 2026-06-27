using System.Diagnostics;
using backgroundControl;

public class EmbeddedCmdTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Constructor_WithPanel_CreatesInstance()
    {
        using var panel = new System.Windows.Forms.Panel();
        using var mgr = new EmbeddedCmdManager(panel);
        mgr.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Constructor_NullPanel_ThrowsArgumentNullException()
    {
        Action act = () => new EmbeddedCmdManager(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void StartCmd_ProcessExists()
    {
        using var panel = new System.Windows.Forms.Panel();
        using var mgr = new EmbeddedCmdManager(panel);

        var cmdProcess = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        cmdProcess.Should().NotBeNull();
        cmdProcess!.HasExited.Should().BeFalse();

        cmdProcess.Kill(entireProcessTree: true);
        cmdProcess.WaitForExit(2000);
        cmdProcess.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void StopCmd_ProcessExits()
    {
        using var panel = new System.Windows.Forms.Panel();
        using var mgr = new EmbeddedCmdManager(panel);

        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        proc.HasExited.Should().BeFalse();

        proc.Kill(entireProcessTree: true);
        proc.WaitForExit(2000);
        proc.HasExited.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void StartCmd_UseShellExecuteFalse_RedirectsDisabled()
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c echo test")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(2000);

        output.Trim().Should().Be("test");
    }
}

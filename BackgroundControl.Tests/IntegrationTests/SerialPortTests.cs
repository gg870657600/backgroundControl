using System.IO.Ports;
using System.Diagnostics;
using Xunit.Sdk;

public class SerialPortTests : IDisposable
{
    private readonly string? _portName;
    private readonly bool _portsAvailable;

    public SerialPortTests()
    {
        // Find the first available COM port
        _portName = SerialPort.GetPortNames().FirstOrDefault();
        _portsAvailable = _portName != null;

        if (_portsAvailable)
        {
            Debug.WriteLine($"Serial port found: {_portName}");
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void OpenPort_Succeeds()
    {
        if (!_portsAvailable) throw new SkipTestException("无可用串口");

        using var port = new SerialPort(_portName, 115200);
        port.Open();
        port.IsOpen.Should().BeTrue();
        port.Close();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void OpenPort_WithDifferentBaudRates_Succeeds()
    {
        if (!_portsAvailable) throw new SkipTestException("无可用串口");

        foreach (var baud in new[] { 9600, 19200, 115200 })
        {
            using var port = new SerialPort(_portName, baud);
            port.Open();
            port.IsOpen.Should().BeTrue();
            port.Close();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void OpenPort_ReadWriteTimeout_DoesNotThrow()
    {
        if (!_portsAvailable) throw new SkipTestException("无可用串口");

        using var port = new SerialPort(_portName, 115200)
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000,
        };
        port.Open();
        port.IsOpen.Should().BeTrue();
        port.Close();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ClosePort_IsOpenFalse()
    {
        if (!_portsAvailable) throw new SkipTestException("无可用串口");

        using var port = new SerialPort(_portName, 115200);
        port.Open();
        port.IsOpen.Should().BeTrue();
        port.Close();
        port.IsOpen.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Open_Close_Reopen_Succeeds()
    {
        if (!_portsAvailable) throw new SkipTestException("无可用串口");

        using var port = new SerialPort(_portName, 115200);
        port.Open();
        port.Close();
        port.Open();
        port.IsOpen.Should().BeTrue();
    }

    public void Dispose()
    {
    }
}

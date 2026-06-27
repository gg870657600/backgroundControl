using System.Diagnostics;
using System.Runtime.InteropServices;

public static class Com0ComSetup
{
    private static bool _installed;

    public static void EnsureInstalled()
    {
        if (_installed) return;

        var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "com0com");
        var exe = Path.Combine(toolsDir, Environment.Is64BitOperatingSystem
            ? "Setup_com0com_v3.0.0.0_W7_x64_signed.exe"
            : "Setup_com0com_v3.0.0.0_W7_x86_signed.exe");

        if (!File.Exists(exe))
            throw new FileNotFoundException($"com0com installer not found at {exe}");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "/silent",
            UseShellExecute = true,
            CreateNoWindow = true,
            Verb = "runas",
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(30000);

        _installed = true;
    }

    public static (string Port1, string Port2) CreatePair()
    {
        EnsureInstalled();
        var pair = FindUnusedPair();
        return pair;
    }

    private static (string, string) FindUnusedPair()
    {
        for (int i = 3; i <= 20; i++)
        {
            var p1 = $"COM{i}";
            var p2 = $"COM{i + 1}";
            if (!PortExists(p1) && !PortExists(p2))
                return (p1, p2);
        }
        throw new InvalidOperationException("No free COM port pair found");
    }

    private static bool PortExists(string name)
    {
        try
        {
            using var port = new System.IO.Ports.SerialPort(name);
            port.Open();
            port.Close();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

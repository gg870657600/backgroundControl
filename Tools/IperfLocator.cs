using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace backgroundControl.Tools;

/// <summary>
/// 把内嵌的 iperf3.exe + Cygwin DLLs 释放到 %LOCALAPPDATA%\BackgroundControl\tools\。
/// csproj 中通过 &lt;EmbeddedResource Include="tools\iperf3.exe" /&gt; 等嵌入。
/// </summary>
public static class IperfLocator
{
    private static readonly string TargetDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BackgroundControl", "tools");

    public static readonly string TargetPath = Path.Combine(TargetDir, "iperf3.exe");

    private static readonly string[] RequiredFiles = {
        "iperf3.exe",
        "cygwin1.dll",
        "cygcrypto-3.dll",
        "cygz.dll",
    };

    /// <summary>
    /// 获取 iperf3.exe 的可执行路径。文件不存在时自动从嵌入资源释放。
    /// </summary>
    public static string GetIperfExePath()
    {
        if (File.Exists(TargetPath) && RequiredFiles.All(f => File.Exists(Path.Combine(TargetDir, f))))
            return TargetPath;

        var asm = Assembly.GetExecutingAssembly();
        Directory.CreateDirectory(TargetDir);

        foreach (var fileName in RequiredFiles)
        {
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (resName == null)
                throw new FileNotFoundException(
                    $"Embedded resource '{fileName}' not found. " +
                    $"Ensure <EmbeddedResource Include=\"tools\\{fileName}\" /> is in the .csproj.");

            var targetFile = Path.Combine(TargetDir, fileName);
            using var src = asm.GetManifestResourceStream(resName)
                ?? throw new FileNotFoundException($"Resource stream '{resName}' is null.");
            using var dst = File.Create(targetFile);
            src.CopyTo(dst);
        }

        ToolsLogger.Info(ToolsLogger.Source.System,
            $"Extracted iperf3 files to {TargetDir}");
        return TargetPath;
    }

    public static bool IsAvailable()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceNames()
            .Any(n => n.EndsWith("iperf3.exe", StringComparison.OrdinalIgnoreCase));
    }
}

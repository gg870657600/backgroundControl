using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace backgroundControl.Tools;

/// <summary>
/// 把内嵌的 iperf3.exe 释放到 %LOCALAPPDATA%\BackgroundControl\tools\iperf3.exe。
/// csproj 中通过 &lt;EmbeddedResource Include="tools\iperf3.exe" /&gt; 把 iperf3.exe 嵌入程序集。
/// </summary>
public static class IperfLocator
{
    /// <summary>释放目标路径（%LOCALAPPDATA% 而非 %TEMP%，避免被系统清理）</summary>
    public static readonly string TargetPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BackgroundControl", "tools", "iperf3.exe");

    /// <summary>
    /// 获取 iperf3.exe 的可执行路径。文件不存在时自动从嵌入资源释放。
    /// </summary>
    /// <exception cref="FileNotFoundException">未找到嵌入的 iperf3.exe 资源</exception>
    public static string GetIperfExePath()
    {
        if (File.Exists(TargetPath)) return TargetPath;

        var asm       = Assembly.GetExecutingAssembly();
        var resName   = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("iperf3.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException(
                "Embedded resource 'iperf3.exe' not found. " +
                "Ensure <EmbeddedResource Include=\"tools\\iperf3.exe\" /> is in the .csproj.");

        Directory.CreateDirectory(Path.GetDirectoryName(TargetPath)!);
        using var src = asm.GetManifestResourceStream(resName)
            ?? throw new FileNotFoundException($"Resource stream '{resName}' is null.");
        using var dst = File.Create(TargetPath);
        src.CopyTo(dst);

        ToolsLogger.Info(ToolsLogger.Source.System,
            $"Extracted iperf3.exe to {TargetPath} ({new FileInfo(TargetPath).Length} bytes)");
        return TargetPath;
    }

    /// <summary>检测嵌入资源是否存在（用于 UI 启动前预校验）</summary>
    public static bool IsAvailable()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceNames()
            .Any(n => n.EndsWith("iperf3.exe", StringComparison.OrdinalIgnoreCase));
    }
}

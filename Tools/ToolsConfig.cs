using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace backgroundControl.Tools;

/// <summary>
/// 工具窗口统一配置（HTTP/FTP/iperf3），存放在
/// %APPDATA%\BackgroundControl\tools-config.json
/// </summary>
public class ToolsConfig
{
    public HttpConfig  Http  { get; set; } = new();
    public FtpConfig   Ftp   { get; set; } = new();
    public IperfConfig Iperf { get; set; } = new();

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BackgroundControl", "tools-config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented        = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// 加载配置；文件不存在或损坏时返回默认值
    /// </summary>
    public static ToolsConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new ToolsConfig();
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<ToolsConfig>(json) ?? new ToolsConfig();
        }
        catch
        {
            // 配置损坏时回退到默认，避免阻塞启动
            return new ToolsConfig();
        }
    }

    /// <summary>
    /// 原子写入：先写 .tmp 再 rename，避免半写文件
    /// </summary>
    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
        // 原子替换（Windows 上 File.Move 同盘是原子的）
        if (File.Exists(ConfigPath)) File.Replace(tmp, ConfigPath, null);
        else                        File.Move(tmp, ConfigPath);
    }
}

public class HttpConfig
{
    public bool   Enabled         { get; set; } = false;
    public int    Port            { get; set; } = 8080;
    public string RootDir         { get; set; } = @"C:\";
    public string Username        { get; set; } = "admin";
    public string Password        { get; set; } = "Changeme_123";
    /// <summary>HTTP 上传大小限制（字节），默认 10GB</summary>
    public long   MaxUploadBytes  { get; set; } = 10L * 1024 * 1024 * 1024;
}

public class FtpConfig
{
    public bool   Enabled         { get; set; } = false;
    public int    Port            { get; set; } = 2121;
    public string RootDir         { get; set; } = @"C:\";
    public int    PassiveStart    { get; set; } = 50000;
    public int    PassiveEnd      { get; set; } = 50100;
    public string Username        { get; set; } = "admin";
    public string Password        { get; set; } = "Changeme_123";
    public bool   AllowAnonymous  { get; set; } = false;
}

public class IperfConfig
{
    /// <summary>"auto" = 走 IperfLocator（从嵌入资源释放）</summary>
    public string IperfExePath    { get; set; } = "auto";
    public int    DefaultPort     { get; set; } = 5201;
}

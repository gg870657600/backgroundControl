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
    public static ToolsConfig Load(string? configPath = null)
    {
        var path = configPath ?? ConfigPath;
        try
        {
            if (!File.Exists(path)) return new ToolsConfig();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ToolsConfig>(json) ?? new ToolsConfig();
        }
        catch
        {
            return new ToolsConfig();
        }
    }

    /// <summary>
    /// 原子写入：先写 .tmp 再 rename，避免半写文件
    /// </summary>
    public void Save(string? configPath = null)
    {
        var path = configPath ?? ConfigPath;
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else                   File.Move(tmp, path);
    }
}

public class HttpConfig
{
    public bool   Enabled         { get; set; } = false;
    public int    Port            { get; set; } = 8080;
    public string RootDir         { get; set; } = @"C:\";
    /// <summary>HTTP 上传大小限制（字节），默认 10GB</summary>
    public long   MaxUploadBytes  { get; set; } = 10L * 1024 * 1024 * 1024;
}

public class FtpConfig
{
    public bool   Enabled         { get; set; } = false;
    public int    Port            { get; set; } = 2121;
    public string RootDir         { get; set; } = @"C:\";
    public int    PassiveStart    { get; set; } = 50000;
    public int    PassiveEnd      { get; set; } = 51000;
    /// <summary>FTP 是否允许匿名登录（用户名 anonymous，密码任意）</summary>
    public bool   AllowAnonymous  { get; set; } = true;
}

public class IperfConfig
{
    /// <summary>"auto" = 走 IperfLocator（从嵌入资源释放）</summary>
    public string IperfExePath    { get; set; } = "auto";
    public int    DefaultPort     { get; set; } = 5201;
}

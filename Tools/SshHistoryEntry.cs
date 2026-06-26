using System;

namespace backgroundControl.Tools;

public class SshHistoryEntry
{
    public string   ConnectionType { get; set; } = "SSH";
    public string   Ip       { get; set; } = "";
    public int      Port     { get; set; } = 22;
    public string   Username { get; set; } = "";
    public string   Password { get; set; } = "";
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
}

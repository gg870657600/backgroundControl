using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace backgroundControl.Tools;

public static class SshHistoryManager
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BackgroundControl", "ssh-history.json");

    private const int MaxEntries = 30;

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BackgroundControl-SshHistory");

    public static List<SshHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<SshHistoryEntry>();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<SshHistoryEntry>>(json) ?? new List<SshHistoryEntry>();
        }
        catch
        {
            return new List<SshHistoryEntry>();
        }
    }

    public static void Save(List<SshHistoryEntry> entries)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries));
        if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
        else File.Move(tmp, FilePath);
    }

    public static string EncryptPassword(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        var enc = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    public static string DecryptPassword(string encrypted)
    {
        var enc = Convert.FromBase64String(encrypted);
        var bytes = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    public static event Action? HistoryChanged;

    public static void RecordConnection(string ip, int port, string username, string password, string connectionType = "SSH")
    {
        var list = Load();

        var existing = list.FirstOrDefault(e =>
            string.Equals(e.Ip, ip, StringComparison.OrdinalIgnoreCase) &&
            e.ConnectionType == connectionType &&
            e.Port == port);
        if (existing != null)
            list.Remove(existing);

        list.Insert(0, new SshHistoryEntry
        {
            ConnectionType = connectionType,
            Ip       = ip,
            Port     = port,
            Username = username,
            Password = EncryptPassword(password),
            LastUsed = DateTime.UtcNow,
        });

        if (list.Count > MaxEntries)
            list.RemoveRange(MaxEntries, list.Count - MaxEntries);

        Save(list);
        HistoryChanged?.Invoke();
    }
}

using System.Text.RegularExpressions;

namespace backgroundControl;

public class CommandPattern
{
    public string Name { get; set; }
    public Regex Regex { get; set; }
    public Func<Match, string> CommandBuilder { get; set; }
}

public class IntentRule
{
    public List<string> Keywords { get; set; }
    public List<string> NormalizedKeywords { get; set; }
    public string Command { get; set; }

    public IntentRule(string keywordsCombined, string command)
    {
        Keywords = keywordsCombined.Split(new[] { ' ', '、' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
        NormalizedKeywords = Keywords.Select(k => Regex.Replace(k, @"\s+", "").ToLowerInvariant()).ToList();
        Command = command;
    }
}

public static class CommandClassifier
{
    private static readonly HashSet<string> _linuxCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 文件/目录
        "cd","pwd","ls","ll","dir","cat","touch","cp","mv","rm","rmdir","mkdir",
        "chmod","chown","chgrp","ln","find","grep","df","du","mount","umount",
        "cut","sort","uniq","tr","diff","tee","xargs","comm","cmp","join",
        "paste","split","wc","nl","od","strings","expand","unexpand",
        "realpath","readlink","basename","dirname","mkfifo","mknod","truncate","shred",
        // 进程/任务
        "ps","top","htop","btop","free","uptime","who","w","id","uname","hostname",
        "kill","killall","pgrep","pkill","nice","renice","nohup","fg","bg","jobs",
        "screen","tmux","at","batch","crontab","timeout",
        // 网络
        "ping","ifconfig","ip","netstat","ss","traceroute","tracepath","nslookup",
        "dig","curl","wget","telnet","ssh","ftp","tftp","sftp",
        "nmap","mtr","host","nc","whois","iptables","ufw","arp","route",
        "nload","iftop","tcpdump","dhclient","iwconfig","iw",
        "ssh-keygen","ssh-copy-id","ssh-add","ssh-agent","scp","socat",
        // 存储/磁盘
        "fdisk","parted","mkfs","fsck","blkid","lsblk","eject","sync","dd","badblocks",
        // 系统信息
        "lscpu","lspci","lshw","lsmod","lsusb","lsof","dmesg","dmidecode",
        "sysctl","modprobe","insmod","rmmod","modinfo","depmod",
        "arch","nproc","uname",
        // 包管理
        "apt","apt-get","apt-cache","dpkg","yum","dnf","rpm","pacman","apk","snap",
        // 用户
        "sudo","su","passwd","useradd","usermod","userdel","groupadd","groupmod",
        "groupdel","chsh","chage","whoami","logname","exit","logout",
        // 文本编辑/查看
        "echo","tail","head","more","less","vi","vim","nano","sed","awk",
        "cat","tac","rev",
        // 压缩/归档
        "tar","gzip","gunzip","zip","unzip","xz","unxz","bzip2","bunzip2",
        "zcat","lz4","zstd",
        // Shell 内置
        "alias","unalias","type","env","export","unset","source","exec","eval",
        "set","times","trap","shopt",
        // 开发/容器
        "git","svn","gcc","g++","make","cmake","python","python3","perl",
        "ruby","go","rustc","cargo","node","npm","yarn",
        "docker","podman","kubectl","helm",
        // 日志/服务
        "journalctl","logger","systemctl","service",
        // 终端
        "clear","reset","date","cal","watch","man","which","whereis",
        "tput","stty","resize","script","sh","bash","zsh","fish","ksh","csh",
        // 杂项
        "yes","seq","shuf","time","stdbuf","base64","sleep","usleep",
        "tee","stdbuf",
        // 设备专有
        "bsp","reboot","poweroff","shutdown","halt","init",
    };

    public static bool IsLinuxCommand(string i)
    {
        if (string.IsNullOrWhiteSpace(i)) return false;
        var first = i.Trim().Split().FirstOrDefault();
        if (first == null) return false;
        if (_linuxCommands.Contains(first)) return true;
        if (first.Contains('/') || first.StartsWith(".")) return true;
        return false;
    }

    public static bool IsDirectCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var reg = new Regex(@"^(get|set|dump|calibre)-", RegexOptions.IgnoreCase);
        return reg.IsMatch(input);
    }

    public static string MatchCommand(string userInput, List<CommandPattern> commandPatterns, List<IntentRule> intentRules)
    {
        string trimmed = userInput.Trim();
        if (IsDirectCommand(trimmed)) return trimmed;

        foreach (var p in commandPatterns)
        {
            var m = p.Regex.Match(trimmed);
            if (m.Success) return p.CommandBuilder(m);
        }

        string normalized = trimmed.Replace(" ", "").ToLowerInvariant();

        var scored = intentRules
            .Select(rule => new
            {
                Rule = rule,
                Matches = rule.NormalizedKeywords
                    .Where(kw => kw.Length > 0)
                    .Select(kw => normalized == kw ? 100
                               : normalized.Contains(kw) ? 60 + kw.Length
                               : 0)
                    .Where(s => s > 0)
                    .ToList()
            })
            .Where(x => x.Matches.Count > 0)
            .Select(x => new
            {
                x.Rule,
                BestScore = x.Matches.Max(),
                TotalScore = x.Matches.Sum()
            })
            .OrderByDescending(x => x.BestScore)
            .ThenByDescending(x => x.TotalScore)
            .ToList();

        var best = scored.FirstOrDefault();
        if (best != null) return best.Rule.Command;

        return "UNKNOWN";
    }

    public static (bool isLinux, string finalCmd) ClassifyCommand(string input, List<CommandPattern> commandPatterns, List<IntentRule> intentRules)
    {
        if (IsLinuxCommand(input))
            return (true, input);

        string finalCmd = MatchCommand(input, commandPatterns, intentRules);

        if (finalCmd == "UNKNOWN")
        {
            finalCmd = input;
            return (!IsDirectCommand(input), finalCmd);
        }

        return (false, finalCmd);
    }

    /// <summary>
    /// 判断是否需要切换环境（SSH↔Telnet），以及切换方向。
    /// </summary>
    /// <param name="inTelnet">当前是否在 Telnet 环境</param>
    /// <returns>(needsSwitch, toTelnet, finalCmd)</returns>
    public static (bool needsSwitch, bool toTelnet, string finalCmd) GetSwitchDecision(
        string cmd, List<CommandPattern> patterns, List<IntentRule> rules, bool inTelnet)
    {
        var (isLinux, finalCmd) = ClassifyCommand(cmd, patterns, rules);
        if (isLinux)
            return (inTelnet, false, finalCmd);       // Linux cmd → 需要在 SSH 环境
        else
            return (!inTelnet, true, finalCmd);        // 私有命令 → 需要在 Telnet 环境
    }
}

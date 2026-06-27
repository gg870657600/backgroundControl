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
        "cd","pwd","ls","ll","dir","cat","touch","cp","mv","rm","rmdir","mkdir",
        "chmod","chown","chgrp","ln","find","grep","df","du","mount","umount",
        "ps","top","htop","btop","free","uptime","who","w","id","uname","hostname",
        "ping","ifconfig","ip","netstat","ss","traceroute","nslookup","dig","curl","wget",
        "telnet","ssh","ftp","nload","iftop","tcpdump",
        "kill","killall","systemctl","service","ps","pgrep","pkill",
        "echo","tail","head","more","less","vi","vim","nano","sed","awk",
        "tar","gzip","gunzip","zip","unzip",
        "clear","reset","date","cal","watch","man","which","whereis","sh","bash",
        "bsp","reboot"
    };

    public static bool IsLinuxCommand(string i)
    {
        if (string.IsNullOrWhiteSpace(i)) return false;
        var first = i.Trim().Split().FirstOrDefault();
        return _linuxCommands.Contains(first);
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
}

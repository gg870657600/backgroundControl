using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Terminal.Wpf;

namespace backgroundControl.Tools
{
    public class HighlightRule
    {
        public string Pattern { get; set; } = "";
        public string Color { get; set; } = "31";
        public string Label { get; set; } = "";
    }

    public class HighlightTerminalConnection : ITerminalConnection
    {
        private static readonly string ConfigPath = "highlight-config.json";
        private static readonly string LogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bgctrl_highlight.log");
        private static readonly List<HighlightRule> _rules = LoadRules();
        private readonly ITerminalConnection _inner;

        public event EventHandler<TerminalOutputEventArgs>? TerminalOutput;

        public HighlightTerminalConnection(ITerminalConnection inner)
        {
            _inner = inner;
            _inner.TerminalOutput += OnInnerTerminalOutput;
            Log($"Proxy created, rules={_rules.Count}, inner={inner.GetType().Name}");
        }

        private static void Log(string msg)
        {
            System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..Math.Min(s.Length, max)];

        private void OnInnerTerminalOutput(object? sender, TerminalOutputEventArgs e)
        {
            var before = e.Data;
            var after = ApplyHighlight(before);
            if (before != after)
                Log($"Highlight matched: \"{Truncate(before.Replace("\n","\\n").Replace("\r","\\r"),60)}\" -> \"{Truncate(after.Replace("\n","\\n").Replace("\r","\\r"),80)}\"");
            else if (_rules.Count > 0)
                Log($"No match for: \"{Truncate(before.Replace("\n","\\n").Replace("\r","\\r"),60)}\"");
            TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(after));
        }

        private static string ApplyHighlight(string text)
        {
            if (string.IsNullOrEmpty(text) || _rules.Count == 0) return text;

            foreach (var rule in _rules)
            {
                try
                {
                    var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    text = regex.Replace(text, m => $"\x1b[{rule.Color}m{m.Value}\x1b[0m");
                }
                catch (Exception ex)
                {
                    Log($"Regex error for \"{rule.Pattern}\": {ex.Message}");
                }
            }
            return text;
        }

        private static List<HighlightRule> LoadRules()
        {
            try
            {
                Log($"Looking for config at: {System.IO.Path.GetFullPath(ConfigPath)}");
                if (!System.IO.File.Exists(ConfigPath))
                {
                    Log("Config file not found");
                    return new List<HighlightRule>();
                }
                var json = System.IO.File.ReadAllText(ConfigPath);
                Log($"Config loaded, length={json.Length}");
                var result = JsonSerializer.Deserialize<List<HighlightRule>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Log($"Parsed {result?.Count ?? 0} rules");
                return result ?? new List<HighlightRule>();
            }
            catch (Exception ex)
            {
                Log($"Config load error: {ex.Message}");
                return new List<HighlightRule>();
            }
        }

        public void Start()
        {
            Log("Start() called");
            _inner.Start();
        }

        public void Close()
        {
            Log("Close() called");
            _inner.Close();
        }

        public void Resize(uint rows, uint columns) => _inner.Resize(rows, columns);
        public void WriteInput(string data) => _inner.WriteInput(data);
    }
}

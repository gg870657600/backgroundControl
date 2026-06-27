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
        private static readonly List<HighlightRule> _rules = LoadRules();
        private static readonly List<(Regex Regex, string Color)> _compiledRules = CompileRules(_rules);
        private readonly ITerminalConnection _inner;

        public event EventHandler<TerminalOutputEventArgs>? TerminalOutput;
        public Action<string>? OnRawOutput { get; set; }

        public HighlightTerminalConnection(ITerminalConnection inner)
        {
            _inner = inner;
            _inner.TerminalOutput += OnInnerTerminalOutput;
        }

        private void OnInnerTerminalOutput(object? sender, TerminalOutputEventArgs e)
        {
            OnRawOutput?.Invoke(e.Data);
            var highlighted = ApplyHighlight(e.Data);
            TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(highlighted));
        }

        private static string ApplyHighlight(string text)
        {
            return ApplyHighlight(text, _compiledRules);
        }

        public static string ApplyHighlight(string text, IReadOnlyList<(Regex Regex, string Color)> patterns)
        {
            if (string.IsNullOrEmpty(text) || patterns.Count == 0) return text;

            foreach (var (regex, color) in patterns)
            {
                try
                {
                    text = regex.Replace(text, m => $"\x1b[{color}m{m.Value}\x1b[0m");
                }
                catch { }
            }
            return text;
        }

        public static List<(Regex, string)> CompileRules(List<HighlightRule> rules)
        {
            var list = new List<(Regex, string)>(rules.Count);
            foreach (var rule in rules)
            {
                try
                {
                    list.Add((new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled), rule.Color));
                }
                catch { }
            }
            return list;
        }

        public static List<HighlightRule> LoadRules(string configPath)
        {
            try
            {
                if (!System.IO.File.Exists(configPath)) return new List<HighlightRule>();
                var json = System.IO.File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<List<HighlightRule>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<HighlightRule>();
            }
            catch
            {
                return new List<HighlightRule>();
            }
        }

        private static List<HighlightRule> LoadRules()
        {
            return LoadRules(ConfigPath);
        }

        public void Start() => _inner.Start();
        public void Close() => _inner.Close();
        public void Resize(uint rows, uint columns) => _inner.Resize(rows, columns);
        public void WriteInput(string data) => _inner.WriteInput(data);
    }
}

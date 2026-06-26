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

        public HighlightTerminalConnection(ITerminalConnection inner)
        {
            _inner = inner;
            _inner.TerminalOutput += OnInnerTerminalOutput;
        }

        private void OnInnerTerminalOutput(object? sender, TerminalOutputEventArgs e)
        {
            var highlighted = ApplyHighlight(e.Data);
            TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(highlighted));
        }

        private static string ApplyHighlight(string text)
        {
            if (string.IsNullOrEmpty(text) || _compiledRules.Count == 0) return text;

            foreach (var (regex, color) in _compiledRules)
            {
                try
                {
                    text = regex.Replace(text, m => $"\x1b[{color}m{m.Value}\x1b[0m");
                }
                catch { }
            }
            return text;
        }

        private static List<(Regex, string)> CompileRules(List<HighlightRule> rules)
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

        private static List<HighlightRule> LoadRules()
        {
            try
            {
                if (!System.IO.File.Exists(ConfigPath)) return new List<HighlightRule>();
                var json = System.IO.File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<List<HighlightRule>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<HighlightRule>();
            }
            catch
            {
                return new List<HighlightRule>();
            }
        }

        public void Start() => _inner.Start();
        public void Close() => _inner.Close();
        public void Resize(uint rows, uint columns) => _inner.Resize(rows, columns);
        public void WriteInput(string data) => _inner.WriteInput(data);
    }
}

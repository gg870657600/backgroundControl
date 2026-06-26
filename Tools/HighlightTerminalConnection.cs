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
        private static readonly List<HighlightRule> _rules = LoadRules();
        private readonly ITerminalConnection _inner;
        private static readonly string ConfigPath = "highlight-config.json";

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
            if (string.IsNullOrEmpty(text) || _rules.Count == 0) return text;

            foreach (var rule in _rules)
            {
                try
                {
                    var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    text = regex.Replace(text, m => $"\x1b[{rule.Color}m{m.Value}\x1b[0m");
                }
                catch { }
            }
            return text;
        }

        private static List<HighlightRule> LoadRules()
        {
            try
            {
                if (!System.IO.File.Exists(ConfigPath)) return new List<HighlightRule>();
                var json = System.IO.File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<List<HighlightRule>>(json) ?? new List<HighlightRule>();
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

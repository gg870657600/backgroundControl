using System.Text;
using System.Text.RegularExpressions;

namespace backgroundControl;

public static class AnsiStripper
{
    private static readonly Regex _ansiEscapeRegex = new(@"\x1b\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex _ansiOSCRegex = new(@"\x1b\][^\x07]*(\x07|\x1b\\)", RegexOptions.Compiled);
    private static readonly Regex _controlCharRegex = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F]", RegexOptions.Compiled);

    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        string result = _ansiEscapeRegex.Replace(text, "");
        result = _ansiOSCRegex.Replace(result, "");
        result = _controlCharRegex.Replace(result, "");
        result = result.Replace("\r\n", "\n").Replace("\r", "");

        return result;
    }
}

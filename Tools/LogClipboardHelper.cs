using System.Text;

namespace backgroundControl.Tools;

public static class LogClipboardHelper
{
    public static (string text, bool isEmpty) PrepareLog(StringBuilder cleanOutput)
    {
        string text = cleanOutput.ToString().Replace("\n", Environment.NewLine);
        return (text, string.IsNullOrWhiteSpace(text));
    }
}

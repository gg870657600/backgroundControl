namespace Xunit.Sdk;

public class SkipTestException : Exception
{
    public SkipTestException(string message) : base(message) { }
}

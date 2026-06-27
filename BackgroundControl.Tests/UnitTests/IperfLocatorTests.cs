using backgroundControl.Tools;

public class IperfLocatorTests
{
    [Fact]
    public void IsAvailable_ReturnsTrueForMainAssembly()
    {
        var result = IperfLocator.IsAvailable();
        result.Should().Be(true);
    }
}

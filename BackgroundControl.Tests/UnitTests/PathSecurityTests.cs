using backgroundControl.Tools;

public class PathSecurityTests
{
    [Fact]
    public void ResolveSafePath_WithinRoot_ReturnsFullPath()
    {
        var result = HttpFileServer.ResolveSafePath(@"C:\root", "sub/file.txt");
        result.Should().Be(Path.GetFullPath(@"C:\root\sub\file.txt"));
    }

    [Fact]
    public void ResolveSafePath_RootItself_ReturnsRoot()
    {
        var root = Path.GetFullPath(@"C:\root");
        var result = HttpFileServer.ResolveSafePath(@"C:\root", "");
        result.Should().Be(root + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void ResolveSafePath_PathTraversal_Throws()
    {
        Action act = () => HttpFileServer.ResolveSafePath(@"C:\root", "../outside/file.txt");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetRootFullPath_AppendsSeparator()
    {
        var result = HttpFileServer.GetRootFullPath(@"C:\root");
        result.Should().EndWith(Path.DirectorySeparatorChar.ToString());
    }
}

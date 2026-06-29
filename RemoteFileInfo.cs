namespace backgroundControl;

public class RemoteFileInfo
{
    public string Name { get; set; } = "";
    public string Size { get; set; } = "";
    public DateTime ModifyTime { get; set; }
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
}

namespace PCL_X.Models;

public class GameSettings
{
    public string JavaPath { get; set; } = "java";
    public int MaxMemory { get; set; } = 4096;
    public int MinMemory { get; set; } = 512;
    public int WindowWidth { get; set; } = 854;
    public int WindowHeight { get; set; } = 480;
    public bool FullScreen { get; set; }
    public string JvmArguments { get; set; } = string.Empty;
    public string GameArguments { get; set; } = string.Empty;
    public string ServerIp { get; set; } = string.Empty;
    public int ServerPort { get; set; }
}

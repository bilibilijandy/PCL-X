using System;
using System.Collections.Generic;

namespace PCL_X.Models;

public class MinecraftVersion
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public DateTime ReleaseTime { get; set; }
    public string Sha1 { get; set; } = string.Empty;
    public long ComplianceLevel { get; set; }
    public bool IsInstalled { get; set; }
    public string InstallPath { get; set; } = string.Empty;
}

public class VersionManifest
{
    public LatestInfo Latest { get; set; } = new();
    public List<MinecraftVersion> Versions { get; set; } = new();
}

public class LatestInfo
{
    public string Release { get; set; } = string.Empty;
    public string Snapshot { get; set; } = string.Empty;
}

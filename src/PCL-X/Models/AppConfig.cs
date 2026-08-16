using System;
using System.Collections.Generic;
using PCL_X.Models;

namespace PCL_X.Models;

public class AppConfig
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string CurrentUser { get; set; } = string.Empty;
    public string GameDirectory { get; set; } = string.Empty;
    public List<UserAccount> SavedAccounts { get; set; } = new();

    /// <summary>全局默认设置（未单独配置具体版本时使用）。</summary>
    public GameSettings Settings { get; set; } = new();

    /// <summary>按版本独立的启动设置（key 为版本 ID），对应 PCL2 的「版本设置」。</summary>
    public Dictionary<string, GameSettings> VersionSettings { get; set; } = new();

    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "zh-CN";
}

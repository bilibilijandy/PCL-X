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
    public GameSettings Settings { get; set; } = new();
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "zh-CN";
}

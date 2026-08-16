using System;

namespace PCL_X.Models;

public class UserAccount
{
    public string UserName { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public AccountType Type { get; set; } = AccountType.Offline;
    public DateTime LastLoginTime { get; set; }
    public bool IsSelected { get; set; }
}

public enum AccountType
{
    Offline,
    Microsoft,
    Other
}

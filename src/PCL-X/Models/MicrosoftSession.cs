namespace PCL_X.Models;

/// <summary>微软账号登录过程中间产物的会话数据。</summary>
public class MicrosoftSession
{
    /// <summary>微软 OAuth 访问令牌（用于刷新时重新走 Xbox 链路）。</summary>
    public string MsAccessToken { get; set; } = string.Empty;

    /// <summary>微软刷新令牌（长期有效，用于静默续期）。</summary>
    public string MsRefreshToken { get; set; } = string.Empty;

    /// <summary>Minecraft 服务访问令牌（约 24 小时有效）。</summary>
    public string McAccessToken { get; set; } = string.Empty;

    /// <summary>Minecraft 玩家名。</summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>Minecraft 玩家 UUID。</summary>
    public string Uuid { get; set; } = string.Empty;
}
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PCL_X.Models;

namespace PCL_X.Services;

/// <summary>
/// 微软账号登录服务。
/// 采用「浏览器回显」方式：使用 Minecraft 官方众所周知的客户端 ID（00000000402b5328），
/// 无需在微软开发者平台注册并等待审核（区别于 PCL2 需要自建并送审的 AppID）。
/// 流程：浏览器授权 → 换取 OAuth 令牌 → XBL → XSTS → Minecraft 令牌 → 玩家档案。
/// </summary>
public class MicrosoftAuthService
{
    // Minecraft 官方客户端 ID（公开、众所周知，参考 Minecraft Wiki / Mojang API 文档）
    private const string ClientId = "00000000402b5328";
    private const string RedirectUri = "https://login.live.com/oauth20_desktop.srf";

    private const string AuthorizeUrl = "https://login.live.com/oauth20_authorize.srf";
    private const string TokenUrl = "https://login.live.com/oauth20_token.srf";
    private const string XblAuthUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string McLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string McProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    private readonly HttpClient _http;

    public MicrosoftAuthService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>生成用于在浏览器中打开的授权链接。</summary>
    public string BuildAuthorizeUrl()
    {
        var scope = Uri.EscapeDataString("service::user.auth.xboxlive.com::MBI_SSL");
        return $"{AuthorizeUrl}?client_id={ClientId}&response_type=code&scope={scope}&redirect_uri={Uri.EscapeDataString(RedirectUri)}";
    }

    /// <summary>
    /// 处理用户粘贴回来的回调地址（形如 oauth20_desktop.srf?code=xxx），
    /// 完成整条微软 → Xbox → Minecraft 认证链路。
    /// </summary>
    public async Task<MicrosoftSession> LoginAsync(string callbackUrl)
    {
        var code = ExtractCode(callbackUrl);
        if (string.IsNullOrEmpty(code))
            throw new InvalidOperationException("未能从回调地址中解析出登录代码（code）。请确保粘贴的是 oauth20_desktop.srf 返回的完整地址。");

        var ms = await ExchangeCodeForTokenAsync(code);
        if (string.IsNullOrEmpty(ms.AccessToken))
            throw new InvalidOperationException("微软登录失败：未能获取访问令牌。");

        return await LoginWithAccessTokenAsync(ms.AccessToken, ms.RefreshToken);
    }

    /// <summary>
    /// 使用已有的微软访问令牌（可来自刷新令牌）走完 XBL → XSTS → Minecraft → 档案链路。
    /// </summary>
    public async Task<MicrosoftSession> LoginWithAccessTokenAsync(string msAccessToken, string? msRefreshToken = null)
    {
        var xbl = await AuthenticateXblAsync(msAccessToken);
        var xsts = await GetXstsAsync(xbl.Token);
        var uhs = xbl.Uhs;

        var mc = await LoginWithXboxAsync(uhs, xsts.Token);

        var profile = await GetProfileAsync(mc.AccessToken);
        if (profile == null)
            throw new InvalidOperationException("该微软账号未购买 Minecraft Java 版，无法启动游戏。");

        return new MicrosoftSession
        {
            MsAccessToken = msAccessToken,
            MsRefreshToken = msRefreshToken ?? "",
            McAccessToken = mc.AccessToken,
            PlayerName = profile.Name,
            Uuid = profile.Uuid
        };
    }

    /// <summary>使用微软刷新令牌重新换取访问令牌（静默续期）。</summary>
    public async Task<string?> RefreshAccessTokenAsync(string refreshToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["redirect_uri"] = RedirectUri,
            ["scope"] = "service::user.auth.xboxlive.com::MBI_SSL"
        };

        using var response = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(form));
        if (!response.IsSuccessStatusCode) return null;

        var json = await ReadJsonAsync(response);
        if (json.TryGetProperty("error", out _)) return null;
        if (json.TryGetProperty("access_token", out var at)) return at.GetString();
        return null;
    }

    private static string ExtractCode(string url)
    {
        var idx = url.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;
        var start = idx + "code=".Length;
        var end = url.IndexOf('&', start);
        var code = end < 0 ? url.Substring(start) : url.Substring(start, end - start);
        return Uri.UnescapeDataString(code.Trim());
    }

    private async Task<TokenResponse> ExchangeCodeForTokenAsync(string code)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = "service::user.auth.xboxlive.com::MBI_SSL"
        };

        using var response = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(form));
        var json = await ReadJsonAsync(response);
        if (!response.IsSuccessStatusCode || json.TryGetProperty("error", out _))
        {
            var desc = json.TryGetProperty("error_description", out var e) ? e.GetString() : response.StatusCode.ToString();
            throw new InvalidOperationException($"微软令牌交换失败：{desc}");
        }

        return new TokenResponse
        {
            AccessToken = GetString(json, "access_token"),
            RefreshToken = GetString(json, "refresh_token")
        };
    }

    private async Task<XblResponse> AuthenticateXblAsync(string msAccessToken)
    {
        var payload = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={msAccessToken}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        using var response = await _http.PostAsync(XblAuthUrl, Json(payload));
        var json = await ReadJsonAsync(response);
        if (!response.IsSuccessStatusCode || !json.TryGetProperty("Token", out var tokenProp))
            throw new InvalidOperationException("Xbox Live 认证失败，请确认已启用 Xbox 服务。");

        var uhs = string.Empty;
        if (json.TryGetProperty("DisplayClaims", out var dc) &&
            dc.TryGetProperty("xui", out var xui) && xui.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in xui.EnumerateArray())
                if (x.TryGetProperty("uhs", out var u)) { uhs = u.GetString() ?? ""; break; }
        }

        return new XblResponse { Token = tokenProp.GetString() ?? "", Uhs = uhs };
    }

    private async Task<XstsResponse> GetXstsAsync(string xblToken)
    {
        var payload = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        using var response = await _http.PostAsync(XstsUrl, Json(payload));
        var json = await ReadJsonAsync(response);

        if (json.TryGetProperty("XErr", out var xerr))
        {
            throw new InvalidOperationException(XstsError(xerr.GetInt64()));
        }
        if (!response.IsSuccessStatusCode || !json.TryGetProperty("Token", out var tokenProp))
            throw new InvalidOperationException("XSTS 令牌获取失败。");

        return new XstsResponse { Token = tokenProp.GetString() ?? "" };
    }

    private async Task<McTokenResponse> LoginWithXboxAsync(string uhs, string xstsToken)
    {
        var payload = new
        {
            identityToken = $"XBL3.0 x={uhs};{xstsToken}"
        };

        using var response = await _http.PostAsync(McLoginUrl, Json(payload));
        var json = await ReadJsonAsync(response);
        if (!response.IsSuccessStatusCode || !json.TryGetProperty("access_token", out var at))
            throw new InvalidOperationException("Minecraft 认证失败，该账号可能未购买游戏。");

        return new McTokenResponse { AccessToken = at.GetString() ?? "" };
    }

    private async Task<ProfileResponse?> GetProfileAsync(string mcAccessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, McProfileUrl);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {mcAccessToken}");
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var json = await ReadJsonAsync(response);
        return new ProfileResponse
        {
            Name = GetString(json, "name"),
            Uuid = GetString(json, "id")
        };
    }

    private static string GetString(JsonElement json, string prop)
        => json.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static StringContent Json(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch
        {
            return default;
        }
    }

    private static string XstsError(long code) => code switch
    {
        2148916233 => "该微软账号未拥有 Minecraft Java 版，无法登录。",
        2148916238 => "Xbox Live 在当前地区不可用。",
        _ => $"Xbox 认证失败（XErr {code}）。"
    };

    private sealed class TokenResponse { public string AccessToken { get; set; } = ""; public string RefreshToken { get; set; } = ""; }
    private sealed class XblResponse { public string Token { get; set; } = ""; public string Uhs { get; set; } = ""; }
    private sealed class XstsResponse { public string Token { get; set; } = ""; }
    private sealed class McTokenResponse { public string AccessToken { get; set; } = ""; }
    private sealed class ProfileResponse { public string Name { get; set; } = ""; public string Uuid { get; set; } = ""; }
}
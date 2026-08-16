using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using PCL_X.Models;
using PCL_X.Modules;

namespace PCL_X.Services;

public interface IAuthService
{
    Task<UserAccount> LoginOfflineAsync(string userName);
    string GenerateOfflineUuid(string userName);
    Task<UserAccount?> GetCurrentUserAsync();
    Task SaveUserAsync(UserAccount user);
    Task<List<UserAccount>> ImportPcl2AccountsAsync();
    Task<string> GetMicrosoftAuthorizeUrlAsync();
    Task<UserAccount> LoginMicrosoftAsync(string callbackUrl);
    Task<bool> RefreshMicrosoftAccountAsync(UserAccount account);
}

public class AuthService : IAuthService
{
    private readonly IUserConfigService _configService;
    private readonly MicrosoftAuthService _microsoftAuth;

    public AuthService(IUserConfigService configService, MicrosoftAuthService microsoftAuth)
    {
        _configService = configService;
        _microsoftAuth = microsoftAuth;
    }

    public string GenerateOfflineUuid(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("用户名不能为空", nameof(userName));

        using var md5 = MD5.Create();
        var inputBytes = Encoding.UTF8.GetBytes($"OfflinePlayer:{userName}");
        var hashBytes = md5.ComputeHash(inputBytes);

        hashBytes[6] = (byte)((hashBytes[6] & 0x0f) | 0x30);
        hashBytes[8] = (byte)((hashBytes[8] & 0x3f) | 0x80);

        var guid = new Guid(hashBytes);
        return guid.ToString("N");
    }

    public async Task<UserAccount> LoginOfflineAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("用户名不能为空", nameof(userName));

        var user = new UserAccount
        {
            UserName = userName.Trim(),
            Uuid = GenerateOfflineUuid(userName),
            AccessToken = Guid.NewGuid().ToString("N"),
            Type = AccountType.Offline,
            LastLoginTime = DateTime.Now,
            IsSelected = true
        };

        var config = await _configService.LoadConfigAsync();
        config.CurrentUser = user.UserName;

        var existing = config.SavedAccounts.Find(a => a.UserName == user.UserName && a.Type == AccountType.Offline);
        if (existing != null)
        {
            config.SavedAccounts.Remove(existing);
        }
        config.SavedAccounts.ForEach(a => a.IsSelected = false);
        config.SavedAccounts.Add(user);

        await _configService.SaveConfigAsync(config);
        return user;
    }

    public async Task<UserAccount?> GetCurrentUserAsync()
    {
        var config = await _configService.LoadConfigAsync();
        if (string.IsNullOrEmpty(config.CurrentUser))
            return null;

        return config.SavedAccounts.Find(a => a.IsSelected)
               ?? config.SavedAccounts.Find(a => a.UserName == config.CurrentUser);
    }

    public async Task SaveUserAsync(UserAccount user)
    {
        var config = await _configService.LoadConfigAsync();
        var existing = config.SavedAccounts.Find(a => a.UserName == user.UserName && a.Type == user.Type);
        if (existing != null)
        {
            config.SavedAccounts.Remove(existing);
        }
        config.SavedAccounts.Add(user);
        await _configService.SaveConfigAsync(config);
    }

    /// <summary>
    /// 从原版 PCL2 导入账户（Windows 互通）。
    /// 优先读取 .minecraft\launcher_profiles.json 中的账户；同时读取 PCL2 注册表中的离线用户名。
    /// 仅把不重复的账户并入已保存列表，不会覆盖已有账户。
    /// </summary>
    public async Task<List<UserAccount>> ImportPcl2AccountsAsync()
    {
        var config = await _configService.LoadConfigAsync();
        var imported = new List<UserAccount>();

        // 1) 从游戏目录的 launcher_profiles.json 导入（PCL2 与官方启动器共享）
        var gameDir = _configService.GetGameDirectory();
        foreach (var acc in ModPclConfig.ImportAccountsFromProfiles(gameDir))
        {
            var user = new UserAccount
            {
                UserName = acc.UserName.Trim(),
                Uuid = string.IsNullOrEmpty(acc.Uuid) ? GenerateOfflineUuid(acc.UserName) : acc.Uuid,
                AccessToken = string.IsNullOrEmpty(acc.AccessToken) ? Guid.NewGuid().ToString("N") : acc.AccessToken,
                Type = AccountType.Offline,
                LastLoginTime = DateTime.Now,
                IsSelected = false
            };
            imported.Add(user);
        }

        // 2) 从 PCL2 注册表读取上次离线登录的用户名（Windows）
        if (ModPlatform.RegistryAvailable && ModPclConfig.ReadPcl2LoginType() == 0)
        {
            var legacyName = ModRegistry.ReadString("LoginLegacyName", "");
            if (!string.IsNullOrWhiteSpace(legacyName))
            {
                var user = new UserAccount
                {
                    UserName = legacyName.Trim(),
                    Uuid = GenerateOfflineUuid(legacyName.Trim()),
                    AccessToken = Guid.NewGuid().ToString("N"),
                    Type = AccountType.Offline,
                    LastLoginTime = DateTime.Now,
                    IsSelected = false
                };
                imported.Add(user);
            }
        }

        // 去重并合并进配置
        var changed = false;
        foreach (var user in imported)
        {
            if (config.SavedAccounts.Exists(a => a.Type == user.Type && a.Uuid == user.Uuid)) continue;
            config.SavedAccounts.Add(user);
            changed = true;
        }

        if (changed)
        {
            // 若当前未登录任何用户，自动选中第一个导入的账户
            if (string.IsNullOrEmpty(config.CurrentUser) && config.SavedAccounts.Count > 0)
            {
                config.CurrentUser = config.SavedAccounts[^1].UserName;
                config.SavedAccounts[^1].IsSelected = true;
            }
            await _configService.SaveConfigAsync(config);
        }

        return imported;
    }

    /// <summary>生成用于在浏览器中打开的微软授权链接（浏览器回显方式）。</summary>
    public Task<string> GetMicrosoftAuthorizeUrlAsync()
        => Task.FromResult(_microsoftAuth.BuildAuthorizeUrl());

    /// <summary>
    /// 使用用户粘贴回的回调地址完成微软登录，并保存为正版账户。
    /// </summary>
    public async Task<UserAccount> LoginMicrosoftAsync(string callbackUrl)
    {
        var session = await _microsoftAuth.LoginAsync(callbackUrl);

        var user = new UserAccount
        {
            UserName = session.PlayerName,
            Uuid = session.Uuid,
            AccessToken = session.McAccessToken,
            RefreshToken = session.MsRefreshToken,
            Type = AccountType.Microsoft,
            LastLoginTime = DateTime.Now,
            IsSelected = true
        };

        var config = await _configService.LoadConfigAsync();
        config.CurrentUser = user.UserName;
        config.SavedAccounts.RemoveAll(a => a.Type == AccountType.Microsoft && a.Uuid == user.Uuid);
        config.SavedAccounts.ForEach(a => a.IsSelected = false);
        config.SavedAccounts.Add(user);
        await _configService.SaveConfigAsync(config);

        return user;
    }

    /// <summary>用保存的微软刷新令牌静默续期，返回是否成功。</summary>
    public async Task<bool> RefreshMicrosoftAccountAsync(UserAccount account)
    {
        if (account.Type != AccountType.Microsoft || string.IsNullOrEmpty(account.RefreshToken)) return false;

        var newMsToken = await _microsoftAuth.RefreshAccessTokenAsync(account.RefreshToken);
        if (string.IsNullOrEmpty(newMsToken)) return false;

        // 用新的微软访问令牌重新走 Xbox → Minecraft 链路，拿到新的 MC 令牌
        var refreshed = await _microsoftAuth.LoginWithAccessTokenAsync(newMsToken);
        if (refreshed == null) return false;

        var config = await _configService.LoadConfigAsync();
        var saved = config.SavedAccounts.Find(a => a.Type == AccountType.Microsoft && a.Uuid == account.Uuid);
        if (saved == null) return false;

        saved.AccessToken = refreshed.McAccessToken;
        saved.RefreshToken = refreshed.MsRefreshToken;
        saved.LastLoginTime = DateTime.Now;
        await _configService.SaveConfigAsync(config);

        account.AccessToken = refreshed.McAccessToken;
        account.RefreshToken = refreshed.MsRefreshToken;
        return true;
    }
}

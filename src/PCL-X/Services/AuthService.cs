using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using PCL_X.Models;

namespace PCL_X.Services;

public interface IAuthService
{
    Task<UserAccount> LoginOfflineAsync(string userName);
    string GenerateOfflineUuid(string userName);
    Task<UserAccount?> GetCurrentUserAsync();
    Task SaveUserAsync(UserAccount user);
}

public class AuthService : IAuthService
{
    private readonly IUserConfigService _configService;

    public AuthService(IUserConfigService configService)
    {
        _configService = configService;
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
}

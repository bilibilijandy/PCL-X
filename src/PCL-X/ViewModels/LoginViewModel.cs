using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL_X.Models;
using PCL_X.Services;

namespace PCL_X.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IMessageBoxService _messageBox;
    private readonly IUserConfigService _configService;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<UserAccount> _savedAccounts = new();

    [ObservableProperty]
    private UserAccount? _selectedAccount;

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private string _microsoftUrl = string.Empty;

    [ObservableProperty]
    private string _microsoftCallback = string.Empty;

    [ObservableProperty]
    private bool _isMicrosoftLoggingIn;

    public event EventHandler<UserAccount>? LoginSucceeded;

    public LoginViewModel(IAuthService authService, IMessageBoxService messageBox, IUserConfigService configService)
    {
        _authService = authService;
        _messageBox = messageBox;
        _configService = configService;
    }

    [RelayCommand]
    public async Task LoadAccountsAsync()
    {
        var config = await _configService.LoadConfigAsync();
        SavedAccounts = new ObservableCollection<UserAccount>(config.SavedAccounts);
    }

    [RelayCommand]
    private async Task LoginOfflineAsync()
    {
        if (string.IsNullOrWhiteSpace(UserName))
        {
            await _messageBox.ShowAsync("请输入用户名！", "提示");
            return;
        }

        if (UserName.Length < 3 || UserName.Length > 16)
        {
            await _messageBox.ShowAsync("用户名长度必须在3-16个字符之间！", "提示");
            return;
        }

        IsLoggingIn = true;
        try
        {
            var user = await _authService.LoginOfflineAsync(UserName.Trim());
            LoginSucceeded?.Invoke(this, user);
            await LoadAccountsAsync();
            SelectedAccount = user;
            await _messageBox.ShowAsync(
                $"离线账户登录成功！\n\n用户名: {user.UserName}\nUUID: {user.Uuid}\n\n这是离线模式登录，仅可进入单人游戏和离线服务器。",
                "登录成功");
        }
        catch (Exception ex)
        {
            await _messageBox.ShowAsync($"登录失败: {ex.Message}", "错误");
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    private async Task SelectAccountAsync(UserAccount? account)
    {
        if (account == null) return;
        var config = await _configService.LoadConfigAsync();
        config.CurrentUser = account.UserName;
        config.SavedAccounts.ForEach(a => a.IsSelected = a.Uuid == account.Uuid);
        await _configService.SaveConfigAsync(config);
        LoginSucceeded?.Invoke(this, account);
    }

    [RelayCommand]
    private async Task RemoveAccountAsync(UserAccount? account)
    {
        if (account == null) return;
        var config = await _configService.LoadConfigAsync();
        config.SavedAccounts.RemoveAll(a => a.Uuid == account.Uuid);
        if (config.CurrentUser == account.UserName)
            config.CurrentUser = string.Empty;
        await _configService.SaveConfigAsync(config);
        await LoadAccountsAsync();
    }

    [RelayCommand]
    private async Task LoginMicrosoftAsync()
    {
        // 第一步：获取授权链接并打开浏览器
        MicrosoftUrl = await _authService.GetMicrosoftAuthorizeUrlAsync();
        OpenBrowser(MicrosoftUrl);
        await _messageBox.ShowAsync(
            "将在浏览器中打开微软登录页面。\n\n" +
            "1. 登录你的微软账号（须已购买 Minecraft Java 版）；\n" +
            "2. 登录成功后浏览器地址栏会变成类似 oauth20_desktop.srf?code=xxxx 的地址；\n" +
            "3. 将该【完整地址】复制，粘贴到下方输入框，再点击「完成登录」。",
            "微软登录");
    }

    [RelayCommand]
    private async Task CompleteMicrosoftLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(MicrosoftCallback))
        {
            await _messageBox.ShowAsync("请先在浏览器登录，并把回调地址粘贴到输入框中。", "提示");
            return;
        }

        IsMicrosoftLoggingIn = true;
        try
        {
            var user = await _authService.LoginMicrosoftAsync(MicrosoftCallback.Trim());
            LoginSucceeded?.Invoke(this, user);
            MicrosoftCallback = string.Empty;
            await LoadAccountsAsync();
            SelectedAccount = user;
            await _messageBox.ShowAsync(
                $"微软账号登录成功！\n\n玩家名: {user.UserName}\nUUID: {user.Uuid}",
                "登录成功");
        }
        catch (Exception ex)
        {
            await _messageBox.ShowAsync($"微软登录失败: {ex.Message}", "错误");
        }
        finally
        {
            IsMicrosoftLoggingIn = false;
        }
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
                Verb = "open"
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    [RelayCommand]
    private Task NotImplemented(string featureName) => _messageBox.ShowNotImplementedAsync(featureName);
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PCL_X.Models;
using PCL_X.Services;

namespace PCL_X.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IDownloadService _downloadService;
    private readonly ILaunchService _launchService;
    private readonly IMessageBoxService _messageBox;
    private readonly IUserConfigService _configService;

    [ObservableProperty]
    private UserAccount? _currentUser;

    [ObservableProperty]
    private string _selectedVersion = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _installedVersions = new();

    [ObservableProperty]
    private ObservableCollection<MinecraftVersion> _availableVersions = new();

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private bool _isLoadingVersions;

    public AppConfig Config { get; private set; } = new();

    public MainViewModel(IAuthService authService, IDownloadService downloadService, ILaunchService launchService, IMessageBoxService messageBox, IUserConfigService configService)
    {
        _authService = authService;
        _downloadService = downloadService;
        _launchService = launchService;
        _messageBox = messageBox;
        _configService = configService;
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        Config = await _configService.LoadConfigAsync();
        CurrentUser = await _authService.GetCurrentUserAsync();
        await RefreshInstalledVersionsAsync();
        await LoadVersionsAsync();
    }

    [RelayCommand]
    private async Task RefreshInstalledVersionsAsync()
    {
        var list = await _downloadService.GetInstalledVersionsAsync();
        InstalledVersions = new ObservableCollection<string>(list);
        if (!string.IsNullOrEmpty(Config.CurrentVersion) && InstalledVersions.Contains(Config.CurrentVersion))
        {
            SelectedVersion = Config.CurrentVersion;
        }
        else if (InstalledVersions.Count > 0)
        {
            SelectedVersion = InstalledVersions[0];
        }
    }

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        IsLoadingVersions = true;
        StatusText = "正在加载版本列表...";
        try
        {
            var manifest = await _downloadService.FetchVersionManifestAsync();
            AvailableVersions = new ObservableCollection<MinecraftVersion>(manifest.Versions.Take(50));
            StatusText = $"已加载 {AvailableVersions.Count} 个可用版本";
        }
        catch (Exception ex)
        {
            StatusText = "加载版本列表失败";
            await _messageBox.ShowAsync($"加载版本列表失败: {ex.Message}", "错误");
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    [RelayCommand]
    private async Task OpenLoginWindowAsync()
    {
        await _messageBox.ShowAsync("请在左侧「账户」面板中添加离线用户登录", "提示");
    }

    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        if (CurrentUser == null || string.IsNullOrWhiteSpace(CurrentUser.UserName))
        {
            await _messageBox.ShowAsync("请先登录用户！可在「账户」面板中添加离线账户", "无法启动");
            return;
        }

        if (string.IsNullOrEmpty(SelectedVersion))
        {
            await _messageBox.ShowAsync("请先选择或下载一个游戏版本！", "无法启动");
            return;
        }

        if (!await _downloadService.IsVersionInstalledAsync(SelectedVersion))
        {
            await _messageBox.ShowAsync("所选版本未安装，请先在「下载」面板中下载", "无法启动");
            return;
        }

        IsLaunching = true;
        StatusText = $"正在启动 Minecraft {SelectedVersion}...";

        try
        {
            var process = await _launchService.LaunchGameAsync(SelectedVersion, CurrentUser, Config.Settings);
            if (process != null)
            {
                StatusText = $"Minecraft {SelectedVersion} 已启动 (PID: {process.Id})";
                Config.CurrentVersion = SelectedVersion;
                await _configService.SaveConfigAsync(Config);
                process.Exited += async (_, _) =>
                {
                    await App.Services.GetRequiredService<IMessageBoxService>().ShowAsync("游戏已退出", "提示");
                };
            }
            else
            {
                StatusText = "启动失败";
            }
        }
        catch (Exception ex)
        {
            await _messageBox.ShowAsync($"启动失败: {ex.Message}", "错误");
            StatusText = "启动失败";
        }
        finally
        {
            IsLaunching = false;
        }
    }

    [RelayCommand]
    private async Task DownloadVersionAsync(MinecraftVersion? version)
    {
        if (version == null)
        {
            await _messageBox.ShowAsync("请先在版本列表中选择要下载的版本", "提示");
            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;
        StatusText = $"正在下载 {version.Id} ...";

        var progress = new Progress<double>(p =>
        {
            DownloadProgress = p * 100;
            StatusText = $"正在下载 {version.Id} ... {p * 100:F1}%";
        });

        try
        {
            await _downloadService.DownloadVersionAsync(version, progress);
            StatusText = $"{version.Id} 下载完成！";
            version.IsInstalled = true;
            await RefreshInstalledVersionsAsync();
            SelectedVersion = version.Id;
        }
        catch (Exception ex)
        {
            await _messageBox.ShowAsync($"下载失败: {ex.Message}", "错误");
            StatusText = "下载失败";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private async Task NotImplemented(string featureName)
    {
        await _messageBox.ShowNotImplementedAsync(featureName);
    }
}

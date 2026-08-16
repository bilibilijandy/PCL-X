using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL_X.Models;
using PCL_X.Modules;
using PCL_X.Services;

namespace PCL_X.ViewModels;

public partial class LaunchViewModel : ObservableObject
{
    /// <summary>设置面板中「全局默认」的标识（对应 PCL2 的全局设置）。</summary>
    public const string GlobalSettingsKey = "(全局默认)";

    private readonly ILaunchService _launchService;
    private readonly IDownloadService _downloadService;
    private readonly IMessageBoxService _messageBox;
    private readonly IUserConfigService _configService;

    [ObservableProperty]
    private GameSettings _settings = new();

    [ObservableProperty]
    private string _detectedJavaPath = "检测中...";

    [ObservableProperty]
    private string _systemInfo = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _installedVersions = new();

    [ObservableProperty]
    private string _selectedVersion = GlobalSettingsKey;

    [ObservableProperty]
    private string _gameDirectory = string.Empty;

    public bool IsWindows => ModPlatform.IsWindows;

    public LaunchViewModel(ILaunchService launchService, IDownloadService downloadService, IMessageBoxService messageBox, IUserConfigService configService)
    {
        _launchService = launchService;
        _downloadService = downloadService;
        _messageBox = messageBox;
        _configService = configService;
    }

    /// <summary>切换设置面板中的版本时，加载该版本的独立设置。</summary>
    partial void OnSelectedVersionChanged(string value)
    {
        _ = LoadSettingsForSelectedVersionAsync();
    }

    private async Task LoadSettingsForSelectedVersionAsync()
    {
        var key = SelectedVersion;
        if (string.IsNullOrEmpty(key) || key == GlobalSettingsKey)
        {
            var config = await _configService.LoadConfigAsync();
            Settings = config.Settings ?? new GameSettings();
        }
        else
        {
            Settings = await _configService.GetSettingsForVersionAsync(key);
        }
    }

    [RelayCommand]
    public async Task LoadSettingsAsync()
    {
        var config = await _configService.LoadConfigAsync();
        GameDirectory = string.IsNullOrEmpty(config.GameDirectory)
            ? _configService.GetGameDirectory()
            : config.GameDirectory;

        // 已安装版本列表 + 全局默认
        var versions = await _downloadService.GetInstalledVersionsAsync();
        InstalledVersions = new ObservableCollection<string>(new[] { GlobalSettingsKey }.Concat(versions));

        // 优先选中当前使用的版本
        string? target = null;
        if (!string.IsNullOrEmpty(config.CurrentVersion) && versions.Contains(config.CurrentVersion))
            target = config.CurrentVersion;
        SelectedVersion = target ?? GlobalSettingsKey;
        await LoadSettingsForSelectedVersionAsync();

        // 系统检测信息展示
        SystemInfo = $"{ModPlatform.Os}/{ModPlatform.Arch} · {ModPlatform.Rid} · {ModPlatform.RuntimeVersion}";

        var javaPath = await _launchService.FindJavaPathAsync();
        DetectedJavaPath = javaPath ?? "未检测到Java，请手动指定";
        if (string.IsNullOrEmpty(Settings.JavaPath) || Settings.JavaPath == "java")
        {
            Settings.JavaPath = javaPath ?? "java";
        }

        // Windows 下从 PCL2 注册表导入启动设置（互通）
        if (ModPlatform.RegistryAvailable)
        {
            ImportPcl2Settings();
        }
    }

    /// <summary>从原版 PCL2 的注册表导入窗口尺寸 / JVM 参数等设置（仅当 PCL-X 尚未自定义时）。</summary>
    private void ImportPcl2Settings()
    {
        var (w, h) = ModPclConfig.ReadPcl2WindowSize();
        if (Settings.WindowWidth <= 0) Settings.WindowWidth = w;
        if (Settings.WindowHeight <= 0) Settings.WindowHeight = h;

        if (string.IsNullOrWhiteSpace(Settings.JvmArguments))
            Settings.JvmArguments = ModPclConfig.ReadPcl2JvmArgs();
        if (string.IsNullOrWhiteSpace(Settings.GameArguments))
            Settings.GameArguments = ModPclConfig.ReadPcl2GameArgs();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var config = await _configService.LoadConfigAsync();

        // 保存游戏目录
        if (!string.IsNullOrWhiteSpace(GameDirectory))
        {
            config.GameDirectory = GameDirectory.Trim();
        }

        // 保存设置：选中具体版本则保存到该版本独立设置，否则保存到全局默认
        if (!string.IsNullOrEmpty(SelectedVersion) && SelectedVersion != GlobalSettingsKey)
        {
            await _configService.SaveSettingsForVersionAsync(SelectedVersion, Settings);
        }
        else
        {
            config.Settings = Settings;
        }

        await _configService.SaveConfigAsync(config);
        await _messageBox.ShowAsync("设置已保存", "提示");
    }

    [RelayCommand]
    private Task NotImplemented(string featureName) => _messageBox.ShowNotImplementedAsync(featureName);
}
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL_X.Models;
using PCL_X.Services;
using System.Threading.Tasks;

namespace PCL_X.ViewModels;

public partial class LaunchViewModel : ObservableObject
{
    private readonly ILaunchService _launchService;
    private readonly IMessageBoxService _messageBox;
    private readonly IUserConfigService _configService;

    [ObservableProperty]
    private GameSettings _settings = new();

    [ObservableProperty]
    private string _detectedJavaPath = "检测中...";

    public LaunchViewModel(ILaunchService launchService, IMessageBoxService messageBox, IUserConfigService configService)
    {
        _launchService = launchService;
        _messageBox = messageBox;
        _configService = configService;
    }

    [RelayCommand]
    public async Task LoadSettingsAsync()
    {
        var config = await _configService.LoadConfigAsync();
        Settings = config.Settings ?? new GameSettings();

        var javaPath = await _launchService.FindJavaPathAsync();
        DetectedJavaPath = javaPath ?? "未检测到Java，请手动指定";
        if (string.IsNullOrEmpty(Settings.JavaPath) || Settings.JavaPath == "java")
        {
            Settings.JavaPath = javaPath ?? "java";
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var config = await _configService.LoadConfigAsync();
        config.Settings = Settings;
        await _configService.SaveConfigAsync(config);
        await _messageBox.ShowAsync("设置已保存", "提示");
    }

    [RelayCommand]
    private Task NotImplemented(string featureName) => _messageBox.ShowNotImplementedAsync(featureName);
}

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL_X.Models;
using PCL_X.Services;

namespace PCL_X.ViewModels;

public partial class DownloadViewModel : ObservableObject
{
    private readonly IDownloadService _downloadService;
    private readonly IMessageBoxService _messageBox;

    [ObservableProperty]
    private ObservableCollection<MinecraftVersion> _versions = new();

    [ObservableProperty]
    private MinecraftVersion? _selectedVersion;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _versionFilter = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _installedVersions = new();

    public event EventHandler<string>? DownloadCompleted;

    public DownloadViewModel(IDownloadService downloadService, IMessageBoxService messageBox)
    {
        _downloadService = downloadService;
        _messageBox = messageBox;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        StatusText = "加载版本列表...";
        var installed = await _downloadService.GetInstalledVersionsAsync();
        InstalledVersions = new ObservableCollection<string>(installed);

        var manifest = await _downloadService.FetchVersionManifestAsync();
        foreach (var v in manifest.Versions)
        {
            v.IsInstalled = installed.Contains(v.Id);
        }
        Versions = new ObservableCollection<MinecraftVersion>(manifest.Versions);
        StatusText = $"加载完成，共 {Versions.Count} 个版本";
    }

    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        if (SelectedVersion == null)
        {
            await _messageBox.ShowAsync("请先选择一个要下载的版本！", "提示");
            return;
        }

        if (SelectedVersion.IsInstalled)
        {
            await _messageBox.ShowAsync("该版本已经安装过了！", "提示");
            return;
        }

        IsDownloading = true;
        Progress = 0;

        var progress = new Progress<double>(p =>
        {
            Progress = p * 100;
            StatusText = $"下载 {SelectedVersion!.Id} ... {p * 100:F1}%";
        });

        try
        {
            await _downloadService.DownloadVersionAsync(SelectedVersion, progress);
            SelectedVersion.IsInstalled = true;
            StatusText = $"{SelectedVersion.Id} 下载完成！";
            var installed = await _downloadService.GetInstalledVersionsAsync();
            InstalledVersions = new ObservableCollection<string>(installed);
            DownloadCompleted?.Invoke(this, SelectedVersion.Id);
        }
        catch (System.Exception ex)
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
    private Task NotImplemented(string featureName) => _messageBox.ShowNotImplementedAsync(featureName);
}

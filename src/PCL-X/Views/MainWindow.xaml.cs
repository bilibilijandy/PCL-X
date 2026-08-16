using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PCL_X.ViewModels;
using PCL_X.Services;
using PCL_X.Models;
using System;

namespace PCL_X.Views;

public partial class MainWindow : Window
{
    private MainViewModel _vm = null!;
    private LoginViewModel _loginVm = null!;
    private DownloadViewModel _downloadVm = null!;
    private LaunchViewModel _launchVm = null!;
    private ScrollViewer? _loginPanel;
    private ScrollViewer? _downloadPanel;
    private ScrollViewer? _settingsPanel;
    private Panel? _launchLoginForms;
    private Border? _offlineLoginBox;
    private Border? _microsoftLoginBox;
    private Button? _loginTypeMs;
    private Button? _loginTypeOffline;
    private Button? _loginTypeNide;
    private Button? _loginTypeAuth;

    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _loginPanel = this.FindControl<ScrollViewer>("LoginPanel");
        _downloadPanel = this.FindControl<ScrollViewer>("DownloadPanel");
        _settingsPanel = this.FindControl<ScrollViewer>("SettingsPanel");
        _launchLoginForms = this.FindControl<Panel>("LaunchLoginForms");
        _offlineLoginBox = this.FindControl<Border>("OfflineLoginBox");
        _microsoftLoginBox = this.FindControl<Border>("MicrosoftLoginBox");
        _loginTypeMs = this.FindControl<Button>("LoginTypeMs");
        _loginTypeOffline = this.FindControl<Button>("LoginTypeOffline");
        _loginTypeNide = this.FindControl<Button>("LoginTypeNide");
        _loginTypeAuth = this.FindControl<Button>("LoginTypeAuth");
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _vm = (DataContext as MainViewModel)!;

        _loginVm = App.Services.GetRequiredService<LoginViewModel>();
        _loginVm.LoginSucceeded += OnLoginSucceeded;
        if (_loginPanel != null) _loginPanel.DataContext = _loginVm;
        if (_launchLoginForms != null) _launchLoginForms.DataContext = _loginVm;
        await _loginVm.LoadAccountsAsync();

        _downloadVm = App.Services.GetRequiredService<DownloadViewModel>();
        _downloadVm.DownloadCompleted += OnDownloadCompleted;
        if (_downloadPanel != null) _downloadPanel.DataContext = _downloadVm;
        await _downloadVm.RefreshAsync();

        _launchVm = App.Services.GetRequiredService<LaunchViewModel>();
        if (_settingsPanel != null) _settingsPanel.DataContext = _launchVm;
        await _launchVm.LoadSettingsAsync();

        if (_vm != null)
        {
            await _vm.InitializeAsync();
            if (_vm.CurrentUser != null)
                _loginVm.SelectedAccount = _vm.CurrentUser;
        }
    }

    private void OnLoginSucceeded(object? sender, UserAccount user)
    {
        if (_vm != null)
        {
            _vm.CurrentUser = user;
        }
    }

    // 启动页登录方式切换（对应 PCL2 PanTypeOne 单选）：微软正版登录 / 离线登录
    private void ShowLoginType(string type)
    {
        if (_offlineLoginBox != null) _offlineLoginBox.IsVisible = type == "offline";
        if (_microsoftLoginBox != null) _microsoftLoginBox.IsVisible = type == "microsoft";
    }

    private void SetLoginTypePill(string type)
    {
        if (_loginTypeMs != null) _loginTypeMs.Classes.Toggle("selected", type == "microsoft");
        if (_loginTypeOffline != null) _loginTypeOffline.Classes.Toggle("selected", type == "offline");
        if (_loginTypeNide != null) _loginTypeNide.Classes.Toggle("selected", false);
        if (_loginTypeAuth != null) _loginTypeAuth.Classes.Toggle("selected", false);
    }

    private void LoginTypeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string type &&
            (type == "microsoft" || type == "offline"))
        {
            ShowLoginType(type);
            SetLoginTypePill(type);
        }
    }

    private async void OnDownloadCompleted(object? sender, string versionId)
    {
        if (_vm != null)
        {
            await _vm.RefreshInstalledVersionsCommand.ExecuteAsync(null);
            _vm.SelectedVersion = versionId;
        }
    }

    // 标题栏拖动窗口（对应 PCL2 WindowStyle=None 的自定义标题栏）
    private void TitleBarPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    // 最小化 / 关闭（对应 PCL2 BtnTitleMin / BtnTitleClose）
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    // 顶部导航（PCL2 风格：启动/下载/联机/设置/更多）切换对应 Tab
    private void TitleNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.Tag is string s && int.TryParse(s, out var index))
        {
            var tabs = this.FindControl<TabControl>("MainTabs");
            if (tabs != null && index >= 0 && index < tabs.Items.Count)
                tabs.SelectedIndex = index;
        }
    }

    private async void NotImplementedClick(object sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.Tag is string name)
        {
            var mb = App.Services.GetRequiredService<IMessageBoxService>();
            await mb.ShowNotImplementedAsync(name);
        }
    }

    private async void BrowseGameDirectoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择游戏目录",
                AllowMultiple = false
            });
            if (folders != null && folders.Count > 0)
            {
                if (_launchVm != null)
                {
                    _launchVm.GameDirectory = folders[0].Path.LocalPath;
                }
            }
        }
        catch (Exception ex)
        {
            var mb = App.Services.GetRequiredService<IMessageBoxService>();
            await mb.ShowAsync($"选择目录失败: {ex.Message}", "错误");
        }
    }
}

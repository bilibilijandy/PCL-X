using Avalonia;
using Avalonia.Controls;
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
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _vm = (DataContext as MainViewModel)!;

        _loginVm = App.Services.GetRequiredService<LoginViewModel>();
        _loginVm.LoginSucceeded += OnLoginSucceeded;
        if (_loginPanel != null) _loginPanel.DataContext = _loginVm;
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

    private async void OnDownloadCompleted(object? sender, string versionId)
    {
        if (_vm != null)
        {
            await _vm.RefreshInstalledVersionsCommand.ExecuteAsync(null);
            _vm.SelectedVersion = versionId;
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
}

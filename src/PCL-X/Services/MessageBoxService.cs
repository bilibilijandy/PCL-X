using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace PCL_X.Services;

public interface IMessageBoxService
{
    Task ShowAsync(string message, string title = "提示");
    Task<bool> ShowNotImplementedAsync(string featureName);
}

public class MessageBoxService : IMessageBoxService
{
    public async Task ShowAsync(string message, string title = "提示")
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = desktop.MainWindow;
                if (window != null)
                {
                    await ShowMessageBox(window, title, message);
                    return;
                }
            }
            Console.WriteLine($"[{title}] {message}");
        });
    }

    public async Task<bool> ShowNotImplementedAsync(string featureName)
    {
        await ShowAsync($"功能「{featureName}」暂未开发，敬请期待后续版本！", "功能未开放");
        return false;
    }

    private static async Task ShowMessageBox(Window parent, string title, string message)
    {
        var window = new Window
        {
            Title = title,
            Width = 420,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.Full
        };

        var panel = new StackPanel { Margin = new Thickness(20) };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var button = new Button
        {
            Content = "确定",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Padding = new Thickness(30, 8),
            MinWidth = 100
        };
        button.Click += (_, _) => window.Close();

        panel.Children.Add(text);
        panel.Children.Add(button);
        window.Content = panel;

        try { await window.ShowDialog(parent); }
        catch { window.Show(); }
    }
}

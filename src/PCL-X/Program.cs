using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PCL_X;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 全局异常捕获：把未处理的异常写入崩溃日志，便于定位启动即崩问题
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            WriteCrashLog(e.ExceptionObject as Exception, "UnhandledException");
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            WriteCrashLog(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex, "Main");
            throw;
        }
    }

    /// <summary>把异常写入 %APPDATA%\.pcl-x\crash.log（Windows / macOS / Linux 通用）。</summary>
    private static void WriteCrashLog(Exception? ex, string source)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".pcl-x");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* 日志写入失败时忽略，避免二次崩溃 */ }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
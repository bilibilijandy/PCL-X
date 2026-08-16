using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PCL_X.Modules;

/// <summary>系统平台枚举，与 Minecraft 的 natives 命名一致。</summary>
public enum LauncherOs
{
    Windows,
    Osx,
    Linux
}

/// <summary>CPU 架构枚举。</summary>
public enum LauncherArch
{
    X86,
    X64,
    Arm,
    Arm64
}

/// <summary>
/// 系统检测模块（对应 PCL2 的 Modules/Base 中的平台判断）。
/// 统一封装操作系统 / 架构 / 各平台路径 / Minecraft natives 分类。
/// </summary>
public static class ModPlatform
{
    public static LauncherOs Os { get; }
    public static LauncherArch Arch { get; }

    public static bool IsWindows => Os == LauncherOs.Windows;
    public static bool IsOsx => Os == LauncherOs.Osx;
    public static bool IsLinux => Os == LauncherOs.Linux;

    /// <summary>.NET 运行时完整版本。</summary>
    public static string RuntimeVersion => RuntimeInformation.FrameworkDescription;

    /// <summary>操作系统描述（如 "win-x64"）。</summary>
    public static string OsDescription => RuntimeInformation.OSDescription.Trim();

    /// <summary>RID 风格标识，如 win-x64 / osx-arm64 / linux-x64。</summary>
    public static string Rid =>
        $"{Os switch { LauncherOs.Windows => "win", LauncherOs.Osx => "osx", _ => "linux" }}-{Arch switch { LauncherArch.X86 => "x86", LauncherArch.X64 => "x64", LauncherArch.Arm => "arm", _ => "arm64" }}";

    static ModPlatform()
    {
        Os = OperatingSystem.IsWindows() ? LauncherOs.Windows
           : OperatingSystem.IsMacOS() ? LauncherOs.Osx
           : LauncherOs.Linux;

        Arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => LauncherArch.X86,
            Architecture.X64 => LauncherArch.X64,
            Architecture.Arm => LauncherArch.Arm,
            Architecture.Arm64 => LauncherArch.Arm64,
            _ => LauncherArch.X64
        };
    }

    /// <summary>Minecraft 使用的 OS 名称（规则过滤用）。</summary>
    public static string MinecraftOsName => Os switch
    {
        LauncherOs.Windows => "windows",
        LauncherOs.Osx => "osx",
        _ => "linux"
    };

    /// <summary>Minecraft 规则中使用的架构名（规则过滤用）。</summary>
    public static string MinecraftOsArch => Arch switch
    {
        LauncherArch.X86 => "x86",
        LauncherArch.X64 => "x86_64",
        LauncherArch.Arm => "arm",
        _ => "aarch64"
    };

    /// <summary>natives 分类器后缀中的架构数字（32/64）。</summary>
    public static string NativeArchSuffix => Arch is LauncherArch.X86 ? "32" : "64";

    /// <summary>当前系统原生库文件扩展名。</summary>
    public static string NativeLibExtension => Os switch
    {
        LauncherOs.Windows => ".dll",
        LauncherOs.Osx => ".dylib",
        _ => ".so"
    };

    /// <summary>java 可执行文件名。</summary>
    public static string JavaExeName => OperatingSystem.IsWindows() ? "java.exe" : "java";

    /// <summary>
    /// 各平台通用的 Minecraft 游戏目录（与 PCL2 / 官方启动器共享）。
    /// Windows: %AppData%\.minecraft；macOS: ~/Library/Application Support/minecraft；Linux: ~/.minecraft
    /// </summary>
    public static string DefaultGameDirectory
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (IsOsx)
                return Path.Combine(home, "Library", "Application Support", "minecraft");
            if (IsWindows)
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
            return Path.Combine(home, ".minecraft");
        }
    }

    /// <summary>PCL2 的配置目录（Windows 为 %AppData%\PCL）。</summary>
    public static string Pcl2AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL");

    /// <summary>是否可访问 Windows 注册表（仅 Windows 有效）。</summary>
    public static bool RegistryAvailable => IsWindows;
}
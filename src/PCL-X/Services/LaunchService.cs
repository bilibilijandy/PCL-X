using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PCL_X.Models;
using PCL_X.Modules;

namespace PCL_X.Services;

public interface ILaunchService
{
    Task<Process?> LaunchGameAsync(string versionId, UserAccount user, GameSettings? settings = null);
    Task<string> GenerateLaunchArgumentsAsync(string versionId, UserAccount user, GameSettings? settings = null);
    Task<List<string>> FindJavaPathsAsync();
    Task<string?> FindJavaPathAsync();
}

public class LaunchService : ILaunchService
{
    private readonly IUserConfigService _configService;

    public LaunchService(IUserConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>查找系统可用的 Java 路径（含 PCL2 注册表中的 Java 列表）。</summary>
    public async Task<List<string>> FindJavaPathsAsync()
    {
        return await Task.Run(() =>
        {
            var found = new List<string>();
            var exe = ModPlatform.JavaExeName;

            // 1) JAVA_HOME
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                var p = Path.Combine(javaHome, "bin", exe);
                if (File.Exists(p) && !found.Contains(p)) found.Add(p);
            }

            // 2) PCL2 注册表中的 Java 列表（Windows 互通）
            foreach (var p in ModPclConfig.ReadPcl2JavaPaths())
            {
                if (File.Exists(p) && !found.Contains(p)) found.Add(p);
            }

            // 3) 扫描常见安装目录
            if (ModPlatform.IsWindows)
            {
                var paths = new[]
                {
                    @"C:\Program Files\Java",
                    @"C:\Program Files (x86)\Java",
                    @"C:\Program Files\Eclipse Adoptium",
                    @"C:\Program Files\Microsoft",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft")
                };
                foreach (var basePath in paths.Distinct().Where(Directory.Exists))
                {
                    foreach (var java in SearchJavaExe(basePath, exe))
                    {
                        if (!found.Contains(java)) found.Add(java);
                    }
                }
            }
            else
            {
                try
                {
                    var proc = new ProcessStartInfo("which", "java")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false
                    };
                    using var p = Process.Start(proc);
                    p?.WaitForExit(5000);
                    var output = p?.StandardOutput.ReadToEnd()?.Trim();
                    if (!string.IsNullOrEmpty(output) && File.Exists(output) && !found.Contains(output))
                        found.Add(output.Trim());
                }
                catch { }
            }

            if (found.Count == 0) found.Add(ModPlatform.JavaExeName);
            return found;
        });
    }

    private static IEnumerable<string> SearchJavaExe(string basePath, string exeName)
    {
        try
        {
            return Directory.GetDirectories(basePath)
                .Select(d => Path.Combine(d, "bin", exeName))
                .Where(File.Exists);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    public async Task<string?> FindJavaPathAsync()
    {
        var paths = await FindJavaPathsAsync();
        return paths.FirstOrDefault(p => p != ModPlatform.JavaExeName) ?? ModPlatform.JavaExeName;
    }

    public async Task<string> GenerateLaunchArgumentsAsync(string versionId, UserAccount user, GameSettings? settings = null)
    {
        var gameDir = _configService.GetGameDirectory();
        // 未显式传入设置时，使用该版本的独立设置（无独立设置则回退到全局默认）
        settings ??= await _configService.GetSettingsForVersionAsync(versionId);

        var versionDir = Path.Combine(gameDir, "versions", versionId);
        var jsonPath = Path.Combine(versionDir, $"{versionId}.json");
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("版本配置文件不存在，请先下载游戏版本", jsonPath);

        var nativeDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativeDir);

        var json = await File.ReadAllTextAsync(jsonPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var clientJar = Path.Combine(versionDir, $"{versionId}.jar");
        var libraries = new List<string> { clientJar };

        // 解析 classpath：普通库 + natives 分类器 jar
        var nativeJars = new List<string>();
        if (root.TryGetProperty("libraries", out var libs))
        {
            foreach (var lib in libs.EnumerateArray())
            {
                var resolved = ModVersion.ResolveLibrary(lib);
                if (resolved == null) continue;

                var lp = Path.Combine(gameDir, "libraries", resolved.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(lp)) continue;

                if (resolved.IsNative)
                    nativeJars.Add(lp);
                else
                    libraries.Add(lp);
            }
        }

        // 若 natives 尚未解压，则解压（不同系统提取的文件不同）
        ModNative.ClearNativeDir(nativeDir);
        foreach (var jar in nativeJars)
        {
            await Task.Run(() => ModNative.ExtractNativeJar(jar, nativeDir, null));
        }

        libraries.AddRange(nativeJars);

        var javaPath = string.IsNullOrEmpty(settings.JavaPath) || settings.JavaPath == ModPlatform.JavaExeName
            ? (await FindJavaPathAsync() ?? ModPlatform.JavaExeName)
            : settings.JavaPath;

        var mainClass = root.TryGetProperty("mainClass", out var mc) ? mc.GetString() ?? "" : "net.minecraft.client.main.Main";

        var separator = OperatingSystem.IsWindows() ? ";" : ":";
        var classpath = string.Join(separator, libraries);

        var gameArgs = new List<string>();
        if (root.TryGetProperty("minecraftArguments", out var legacyArgs))
        {
            gameArgs.AddRange((legacyArgs.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        else if (root.TryGetProperty("arguments", out var argsObj) && argsObj.TryGetProperty("game", out var gameArr))
        {
            foreach (var a in gameArr.EnumerateArray())
            {
                if (a.ValueKind == JsonValueKind.String)
                    gameArgs.Add(a.GetString() ?? "");
                else if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("rules", out _))
                {
                    // 带 rules 的 game 参数：按当前系统判断是否加入
                    if (a.TryGetProperty("value", out var val))
                    {
                        if (val.ValueKind == JsonValueKind.String)
                            gameArgs.Add(val.GetString() ?? "");
                        else if (val.ValueKind == JsonValueKind.Array)
                            foreach (var s in val.EnumerateArray())
                                if (s.ValueKind == JsonValueKind.String)
                                    gameArgs.Add(s.GetString() ?? "");
                    }
                }
            }
        }

        var assetIndexId = "1.8";
        if (root.TryGetProperty("assetIndex", out var ai) && ai.TryGetProperty("id", out var idProp))
            assetIndexId = idProp.GetString() ?? "1.8";

        var jvmArgs = new List<string>
        {
            $"-Xmx{settings.MaxMemory}M",
            $"-Xms{settings.MinMemory}M",
            $"-Djava.library.path=\"{nativeDir}\"",
            $"-Dminecraft.client.jar=\"{clientJar}\"",
            "-cp",
            $"\"{classpath}\"",
            mainClass
        };

        // macOS 需要 -XstartOnFirstThread 才能运行原生窗口
        if (ModPlatform.IsOsx)
            jvmArgs.Insert(0, "-XstartOnFirstThread");

        if (!string.IsNullOrWhiteSpace(settings.JvmArguments))
        {
            jvmArgs.InsertRange(0, settings.JvmArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var replacedArgs = ReplaceGameArgs(gameArgs, user, gameDir, versionDir, assetIndexId, versionId, settings);

        if (!string.IsNullOrWhiteSpace(settings.GameArguments))
        {
            replacedArgs.AddRange(settings.GameArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var fullArgs = string.Join(" ", jvmArgs.Concat(replacedArgs));
        return fullArgs;
    }

    private static List<string> ReplaceGameArgs(IEnumerable<string> args, UserAccount user, string gameDir, string versionDir, string assetsId, string versionId, GameSettings settings)
    {
        var result = new List<string>();
        foreach (var a in args)
        {
            var replaced = a
                .Replace("${auth_player_name}", user.UserName)
                .Replace("${version_name}", versionId)
                .Replace("${game_directory}", $"\"{gameDir}\"")
                .Replace("${assets_root}", $"\"{Path.Combine(gameDir, "assets")}\"")
                .Replace("${assets_index_name}", assetsId)
                .Replace("${auth_uuid}", user.Uuid)
                .Replace("${auth_access_token}", user.AccessToken)
                .Replace("${user_type}", user.Type == AccountType.Microsoft ? "msa" : "legacy")
                .Replace("${version_type}", "PCL-X")
                .Replace("${resolution_width}", settings.WindowWidth.ToString())
                .Replace("${resolution_height}", settings.WindowHeight.ToString());
            result.Add(replaced);
        }
        return result;
    }

    public async Task<Process?> LaunchGameAsync(string versionId, UserAccount user, GameSettings? settings = null)
    {
        if (string.IsNullOrEmpty(versionId))
            throw new ArgumentException("版本ID不能为空", nameof(versionId));
        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(user.UserName))
            throw new ArgumentException("用户名不能为空", nameof(user));

        var config = await _configService.LoadConfigAsync();
        settings ??= await _configService.GetSettingsForVersionAsync(versionId);

        settings.JavaPath = string.IsNullOrEmpty(settings.JavaPath) || settings.JavaPath == ModPlatform.JavaExeName
            ? (await FindJavaPathAsync() ?? ModPlatform.JavaExeName)
            : settings.JavaPath;

        var args = await GenerateLaunchArgumentsAsync(versionId, user, settings);
        var gameDir = config.GameDirectory;

        var startInfo = new ProcessStartInfo
        {
            FileName = settings.JavaPath,
            Arguments = args,
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();
        return process;
    }
}
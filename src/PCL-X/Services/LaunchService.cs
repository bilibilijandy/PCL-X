using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PCL_X.Models;

namespace PCL_X.Services;

public interface ILaunchService
{
    Task<Process?> LaunchGameAsync(string versionId, UserAccount user, GameSettings? settings = null);
    Task<string> GenerateLaunchArgumentsAsync(string versionId, UserAccount user, GameSettings? settings = null);
    Task<string?> FindJavaPathAsync();
}

public class LaunchService : ILaunchService
{
    private readonly IUserConfigService _configService;

    public LaunchService(IUserConfigService configService)
    {
        _configService = configService;
    }

    public async Task<string?> FindJavaPathAsync()
    {
        return await Task.Run(() =>
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                var p = Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
                if (File.Exists(p)) return p;
            }

            if (OperatingSystem.IsWindows())
            {
                var paths = new[]
                {
                    @"C:\Program Files\Java",
                    @"C:\Program Files (x86)\Java",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "jdk-17.0.12.7-hotspot")
                };
                foreach (var basePath in paths.Where(Directory.Exists))
                {
                    var java = Directory.GetFiles(basePath, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (java != null) return java;
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
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                        return output;
                }
                catch { }
            }
            return "java";
        });
    }

    public async Task<string> GenerateLaunchArgumentsAsync(string versionId, UserAccount user, GameSettings? settings = null)
    {
        var gameDir = _configService.GetGameDirectory();
        var config = await _configService.LoadConfigAsync();
        settings ??= config.Settings;

        var versionDir = Path.Combine(gameDir, "versions", versionId);
        var jsonPath = Path.Combine(versionDir, $"{versionId}.json");
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("版本配置文件不存在，请先下载游戏版本", jsonPath);

        var nativeDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativeDir);

        var json = await File.ReadAllTextAsync(jsonPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var libraries = new List<string>();
        var clientJar = Path.Combine(versionDir, $"{versionId}.jar");
        libraries.Add(clientJar);

        if (root.TryGetProperty("libraries", out var libs))
        {
            foreach (var lib in libs.EnumerateArray())
            {
                if (lib.TryGetProperty("downloads", out var downloads))
                {
                    if (downloads.TryGetProperty("artifact", out var artifact))
                    {
                        if (artifact.TryGetProperty("path", out var p))
                        {
                            var lp = Path.Combine(gameDir, "libraries", p.GetString() ?? "");
                            if (File.Exists(lp)) libraries.Add(lp);
                        }
                    }
                }
            }
        }

        var javaPath = string.IsNullOrEmpty(settings.JavaPath) || settings.JavaPath == "java"
            ? (await FindJavaPathAsync() ?? "java")
            : settings.JavaPath;

        var mainClass = root.TryGetProperty("mainClass", out var mc) ? mc.GetString() ?? "" : "net.minecraft.client.main.Main";

        var classpath = string.Join(Path.PathSeparator == ';' ? ";" : ":", libraries);

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
        settings ??= config.Settings;

        var config2 = await _configService.LoadConfigAsync();
        settings.JavaPath = string.IsNullOrEmpty(settings.JavaPath) || settings.JavaPath == "java"
            ? (await FindJavaPathAsync() ?? "java")
            : settings.JavaPath;

        var args = await GenerateLaunchArgumentsAsync(versionId, user, settings);
        var gameDir = config2.GameDirectory;

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

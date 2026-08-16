using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PCL_X.Models;
using PCL_X.Modules;

namespace PCL_X.Services;

public interface IUserConfigService
{
    Task<AppConfig> LoadConfigAsync();
    Task SaveConfigAsync(AppConfig config);
    string GetGameDirectory();
    Task<GameSettings> GetSettingsForVersionAsync(string versionId);
    Task SaveSettingsForVersionAsync(string versionId, GameSettings settings);
}

public class UserConfigService : IUserConfigService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".pcl-x");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public string GetGameDirectory()
    {
        // 优先使用用户在设置中自定义的游戏目录；否则使用各平台标准 .minecraft（Windows 下与原版 PCL2 / 官方启动器共享）
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("gameDirectory", out var gd) &&
                    gd.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(gd.GetString()))
                {
                    var custom = gd.GetString()!;
                    Directory.CreateDirectory(custom);
                    return custom;
                }
            }
        }
        catch { }

        var path = ModPlatform.DefaultGameDirectory;
        Directory.CreateDirectory(path);
        return path;
    }

    public async Task<AppConfig> LoadConfigAsync()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            if (!File.Exists(ConfigPath))
            {
                var defaultConfig = new AppConfig
                {
                    GameDirectory = GetGameDirectory()
                };
                await SaveConfigAsync(defaultConfig);
                return defaultConfig;
            }

            var json = await File.ReadAllTextAsync(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            });
            if (config == null) return new AppConfig { GameDirectory = GetGameDirectory() };
            if (string.IsNullOrEmpty(config.GameDirectory))
                config.GameDirectory = GetGameDirectory();
            return config;
        }
        catch
        {
            return new AppConfig { GameDirectory = GetGameDirectory() };
        }
    }

    public async Task SaveConfigAsync(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(ConfigPath, json);
    }

    /// <summary>
    /// 获取指定版本的启动设置。
    /// 若该版本有独立设置则返回之；否则返回全局默认设置（对应 PCL2 的版本设置/全局设置）。
    /// </summary>
    public async Task<GameSettings> GetSettingsForVersionAsync(string versionId)
    {
        var config = await LoadConfigAsync();
        if (!string.IsNullOrEmpty(versionId) &&
            config.VersionSettings.TryGetValue(versionId, out var vs))
        {
            return vs;
        }
        return config.Settings ?? new GameSettings();
    }

    /// <summary>保存指定版本的独立启动设置。</summary>
    public async Task SaveSettingsForVersionAsync(string versionId, GameSettings settings)
    {
        var config = await LoadConfigAsync();
        config.VersionSettings[versionId] = settings;
        await SaveConfigAsync(config);
    }
}

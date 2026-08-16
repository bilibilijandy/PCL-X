using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PCL_X.Models;

namespace PCL_X.Services;

public interface IUserConfigService
{
    Task<AppConfig> LoadConfigAsync();
    Task SaveConfigAsync(AppConfig config);
    string GetGameDirectory();
}

public class UserConfigService : IUserConfigService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".pcl-x");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public string GetGameDirectory()
    {
        var path = Path.Combine(ConfigDir, ".minecraft");
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
}

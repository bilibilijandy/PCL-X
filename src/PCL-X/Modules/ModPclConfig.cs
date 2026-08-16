using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PCL_X.Modules;

namespace PCL_X.Modules;

/// <summary>从 PCL2 / 官方启动器导入的账户信息。</summary>
public class ImportedAccount
{
    public string UserName { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
}

/// <summary>
/// PCL2 配置互通模块。
/// 在 Windows 下读取原版 PCL2 的注册表设置与 .minecraft\launcher_profiles.json 中的账户数据，
/// 实现账户 / Java / 窗口等设置的导入互通。
/// </summary>
public static class ModPclConfig
{
    // ---- PCL2 注册表设置项（来自 PCL2 源码 Settings.vb，Source := Sources.Registry） ----

    /// <summary>PCL2 上次使用的登录类型（0=离线 Legacy，2=通行证，3=Authlib，5=微软）。</summary>
    public static int ReadPcl2LoginType() => ModRegistry.ReadInt("LoginType", 0);

    /// <summary>读取 PCL2 记录的 Java 列表（JSON 数组，含路径与版本）。</summary>
    public static List<string> ReadPcl2JavaPaths()
    {
        var result = new List<string>();
        var json = ModRegistry.ReadString("LaunchArgumentJavaAll", "[]");
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        result.Add(item.GetString() ?? "");
                    else if (item.ValueKind == JsonValueKind.Object &&
                             item.TryGetProperty("Path", out var p))
                        result.Add(p.GetString() ?? "");
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>读取 PCL2 的窗口宽度/高度（默认 854x480）。</summary>
    public static (int Width, int Height) ReadPcl2WindowSize()
        => (ModRegistry.ReadInt("LaunchArgumentWindowWidth", 854),
            ModRegistry.ReadInt("LaunchArgumentWindowHeight", 480));

    /// <summary>读取 PCL2 的自定义 JVM 参数。</summary>
    public static string ReadPcl2JvmArgs()
        => ModRegistry.ReadString("LaunchAdvanceJvm", "");

    /// <summary>读取 PCL2 的自定义游戏参数。</summary>
    public static string ReadPcl2GameArgs()
        => ModRegistry.ReadString("LaunchAdvanceGame", "");

    /// <summary>读取 PCL2 的客户端令牌（clientToken），用于校验账户互通。</summary>
    public static string ReadPcl2ClientToken()
        => ModRegistry.ReadString("Identify", "");

    /// <summary>是否已检测到原版 PCL2 的注册表数据（用于界面提示互通状态）。</summary>
    public static bool HasPcl2RegistryData()
        => ModPclConfig.ReadPcl2LoginType() != 0
            || !string.IsNullOrEmpty(ModRegistry.ReadString("LaunchArgumentJavaAll"))
            || ModRegistry.ReadInt("LaunchArgumentWindowWidth", -1) >= 0;

    /// <summary>
    /// 从游戏目录的 launcher_profiles.json 中导入账户。
    /// PCL2 与官方启动器都会把账户信息以明文写入该文件。
    /// </summary>
    public static List<ImportedAccount> ImportAccountsFromProfiles(string gameDir)
    {
        var result = new List<ImportedAccount>();
        var path = Path.Combine(gameDir, "launcher_profiles.json");
        if (!File.Exists(path)) return result;
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("authenticationDatabase", out var db) &&
                db.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in db.EnumerateObject())
                {
                    var uuid = entry.Name;
                    var account = entry.Value;
                    var username = account.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "";
                    var token = account.TryGetProperty("accessToken", out var at) ? at.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(username)) continue;

                    // 尝试从 profiles 中取 displayName 作为显示名
                    if (account.TryGetProperty("profiles", out var profiles) &&
                        profiles.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prof in profiles.EnumerateObject())
                        {
                            if (prof.Value.TryGetProperty("displayName", out var dn) &&
                                !string.IsNullOrEmpty(dn.GetString()))
                            {
                                username = dn.GetString() ?? username;
                                break;
                            }
                        }
                    }

                    result.Add(new ImportedAccount
                    {
                        UserName = username,
                        Uuid = uuid,
                        AccessToken = token
                    });
                }
            }
        }
        catch { }
        return result;
    }
}
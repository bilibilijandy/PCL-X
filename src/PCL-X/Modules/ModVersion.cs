using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PCL_X.Modules;

namespace PCL_X.Modules;

/// <summary>解析后的单个库。</summary>
public class ResolvedLibrary
{
    /// <summary>库在 gameDir 下的相对路径（用于 libraries 目录或版本目录）。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>下载地址。</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>是否为当前系统的 natives 库。</summary>
    public bool IsNative { get; set; }

    /// <summary>natives 库需要解压到的版本目录下的子目录名。</summary>
    public string NativeSubDir { get; set; } = "natives";

    /// <summary>natives 解压时需排除的文件名（来自 extract.exclude）。</summary>
    public HashSet<string> ExcludeNames { get; set; } = new();
}

/// <summary>
/// 版本清单解析模块（对应 PCL2 的 ModLaunch / ModVersion 逻辑）。
/// 负责按当前系统解析 libraries 的 rules 过滤、natives 分类器与资源路径。
/// </summary>
public static class ModVersion
{
    /// <summary>
    /// 判断一个库在当前系统下是否适用（处理 rules.os 过滤）。
    /// 算法：默认允许；顺序遍历 rules，命中的规则决定该项结果。
    /// </summary>
    public static bool LibraryApplies(JsonElement lib)
    {
        if (!lib.TryGetProperty("rules", out var rules)) return true;

        var allowed = true;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.TryGetProperty("action", out var a) ? a.GetString() : "allow";
            if (OsRuleMatches(rule)) allowed = action == "allow";
        }
        return allowed;
    }

    private static bool OsRuleMatches(JsonElement rule)
    {
        if (!rule.TryGetProperty("os", out var os)) return true;

        if (os.TryGetProperty("name", out var name) && !string.Equals(name.GetString(), ModPlatform.MinecraftOsName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (os.TryGetProperty("arch", out var arch))
        {
            var archStr = arch.GetString() ?? "";
            if (!ArchMatches(archStr)) return false;
        }

        if (os.TryGetProperty("version", out var version) && !string.IsNullOrEmpty(version.GetString()))
        {
            // version 为正则，仅粗略匹配已安装的 Java 版本，此处不深入，视为匹配。
        }

        return true;
    }

    private static bool ArchMatches(string arch)
    {
        var current = ModPlatform.MinecraftOsArch;
        return arch.Equals(current, StringComparison.OrdinalIgnoreCase)
               || (ModPlatform.Arch == LauncherArch.X64 && (arch.Equals("amd64", StringComparison.OrdinalIgnoreCase)))
               || (ModPlatform.Arch == LauncherArch.Arm64 && (arch.Equals("arm64", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 获取某库在当前系统下的 natives 分类器名（如 "natives-windows-64"）。
    /// 若该库没有匹配当前系统的 natives 定义，返回 null。
    /// </summary>
    public static string? GetNativeClassifier(JsonElement lib)
    {
        if (!lib.TryGetProperty("natives", out var natives)) return null;
        if (!natives.TryGetProperty(ModPlatform.MinecraftOsName, out var classifier)) return null;

        var template = classifier.GetString();
        if (string.IsNullOrEmpty(template)) return null;

        return template.Replace("${arch}", ModPlatform.NativeArchSuffix);
    }

    /// <summary>解析 extract.exclude 中的文件名列表（仅取文件名，用于解压时排除）。</summary>
    public static HashSet<string> GetExtractExcludes(JsonElement lib)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (lib.TryGetProperty("extract", out var extract) && extract.TryGetProperty("exclude", out var exclude))
        {
            foreach (var e in exclude.EnumerateArray())
            {
                var s = e.GetString();
                if (!string.IsNullOrEmpty(s)) result.Add(Path.GetFileName(s));
            }
        }
        return result;
    }

    /// <summary>
    /// 解析单个库，返回解析结果；若该库在当前系统不适用则返回 null。
    /// 会正确处理 rules 过滤与 natives 分类器。
    /// </summary>
    public static ResolvedLibrary? ResolveLibrary(JsonElement lib)
    {
        if (!LibraryApplies(lib)) return null;

        // 判断是否为 natives 库
        var isNative = GetNativeClassifier(lib) is string classifier;
        var result = new ResolvedLibrary { IsNative = isNative };

        if (isNative)
        {
            var nativeClassifier = GetNativeClassifier(lib)!;
            if (lib.TryGetProperty("downloads", out var downloads) &&
                downloads.TryGetProperty("classifiers", out var classifiers) &&
                classifiers.TryGetProperty(nativeClassifier, out var cls))
            {
                if (cls.TryGetProperty("path", out var p)) result.Path = p.GetString() ?? "";
                if (cls.TryGetProperty("url", out var u)) result.Url = u.GetString() ?? "";
            }
            result.ExcludeNames = GetExtractExcludes(lib);
        }
        else
        {
            if (lib.TryGetProperty("downloads", out var downloads) &&
                downloads.TryGetProperty("artifact", out var artifact))
            {
                if (artifact.TryGetProperty("path", out var p)) result.Path = p.GetString() ?? "";
                if (artifact.TryGetProperty("url", out var u)) result.Url = u.GetString() ?? "";
            }
            else if (lib.TryGetProperty("name", out var nameProp))
            {
                var name = nameProp.GetString() ?? "";
                var parts = name.Split(':');
                if (parts.Length >= 3)
                {
                    var group = parts[0].Replace('.', '/');
                    var artifactName = parts[1];
                    var ver = parts[2];
                    result.Path = $"{group}/{artifactName}/{ver}/{artifactName}-{ver}.jar";
                    result.Url = $"https://libraries.minecraft.net/{result.Path}";
                }
            }
        }

        if (string.IsNullOrEmpty(result.Path)) return null;
        return result;
    }
}
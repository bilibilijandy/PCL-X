using System;
using Microsoft.Win32;

namespace PCL_X.Modules;

/// <summary>
/// Windows 注册表互通模块（对应 PCL2 的 ModBase 中 ReadReg/WriteReg 逻辑）。
/// PCL2 的核心设置存储在 HKEY_CURRENT_USER\Software\PCLDebug 下。
/// 该模块在 Windows 下读取/写入这些键，实现与原版 PCL2 的数据互通；
/// 在非 Windows 平台自动降级为空实现，不会抛错。
/// </summary>
public static class ModRegistry
{
    /// <summary>PCL2 注册表根路径（来自 PCL2 源码 ModSecret.vb 的 RegFolder = "PCLDebug"）。</summary>
    public const string Pcl2RegKey = @"Software\PCLDebug";

    /// <summary>读取字符串值。key 不存在时返回默认值。</summary>
    public static string ReadString(string key, string defaultValue = "")
    {
        if (!ModPlatform.RegistryAvailable) return defaultValue;
        try
        {
            using var reg = Registry.CurrentUser.OpenSubKey(Pcl2RegKey);
            return reg?.GetValue(key, defaultValue) as string ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>读取整数值。key 不存在时返回默认值。</summary>
    public static int ReadInt(string key, int defaultValue = 0)
    {
        if (!ModPlatform.RegistryAvailable) return defaultValue;
        try
        {
            using var reg = Registry.CurrentUser.OpenSubKey(Pcl2RegKey);
            if (reg?.GetValue(key) is int i) return i;
            if (reg?.GetValue(key) is string s && int.TryParse(s, out var parsed)) return parsed;
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>写入字符串值。失败时静默忽略。</summary>
    public static void WriteString(string key, string value)
    {
        if (!ModPlatform.RegistryAvailable) return;
        try
        {
            using var reg = Registry.CurrentUser.CreateSubKey(Pcl2RegKey, true);
            reg?.SetValue(key, value);
        }
        catch { }
    }

    /// <summary>写入整数值。失败时静默忽略。</summary>
    public static void WriteInt(string key, int value)
    {
        if (!ModPlatform.RegistryAvailable) return;
        try
        {
            using var reg = Registry.CurrentUser.CreateSubKey(Pcl2RegKey, true);
            reg?.SetValue(key, value);
        }
        catch { }
    }

    /// <summary>判断 key 是否存在。</summary>
    public static bool HasValue(string key)
    {
        if (!ModPlatform.RegistryAvailable) return false;
        try
        {
            using var reg = Registry.CurrentUser.OpenSubKey(Pcl2RegKey);
            return reg?.GetValue(key) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>删除 key。失败时静默忽略。</summary>
    public static void DeleteValue(string key)
    {
        if (!ModPlatform.RegistryAvailable) return;
        try
        {
            using var reg = Registry.CurrentUser.OpenSubKey(Pcl2RegKey, true);
            reg?.DeleteValue(key, throwOnMissingValue: false);
        }
        catch { }
    }
}
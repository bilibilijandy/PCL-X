using System;
using System.IO;
using System.IO.Compression;

namespace PCL_X.Modules;

/// <summary>
/// 原生库（natives）处理模块。
/// Minecraft 的 natives 库在不同系统下是不同的（Windows: .dll / macOS: .dylib / Linux: .so），
/// 该模块负责把下载到的 natives jar 解压出当前系统对应的原生库文件。
/// </summary>
public static class ModNative
{
    /// <summary>
    /// 将 natives jar 中的原生库文件解压到目标目录。
    /// 仅提取当前系统对应的原生库扩展名文件，并跳过 exclude 中列出的文件名。
    /// </summary>
    /// <param name="jarPath">natives jar 完整路径。</param>
    /// <param name="nativeDir">解压目标目录。</param>
    /// <param name="excludeNames">需要排除的文件名集合（可为 null）。</param>
    public static int ExtractNativeJar(string jarPath, string nativeDir, System.Collections.Generic.HashSet<string>? excludeNames)
    {
        if (!File.Exists(jarPath)) return 0;

        Directory.CreateDirectory(nativeDir);
        var extracted = 0;

        using var zip = ZipFile.OpenRead(jarPath);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/")) continue;

            var fileName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(fileName)) continue;
            if (excludeNames != null && excludeNames.Contains(fileName)) continue;

            var ext = Path.GetExtension(fileName);
            if (!IsNativeFile(ext)) continue;

            var dest = Path.Combine(nativeDir, fileName);
            entry.ExtractToFile(dest, overwrite: true);
            extracted++;
        }

        return extracted;
    }

    /// <summary>判断某扩展名是否为当前系统可用的原生库文件。</summary>
    public static bool IsNativeFile(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext == ModPlatform.NativeLibExtension;
    }

    /// <summary>清空某个 natives 目录（在重新解压前调用，避免残留旧系统文件）。</summary>
    public static void ClearNativeDir(string nativeDir)
    {
        if (!Directory.Exists(nativeDir)) return;
        foreach (var file in Directory.GetFiles(nativeDir))
        {
            try { File.Delete(file); } catch { }
        }
    }
}
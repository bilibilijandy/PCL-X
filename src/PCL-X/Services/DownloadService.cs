using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using PCL_X.Models;
using PCL_X.Modules;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PCL_X.Services;

public interface IDownloadService
{
    Task<VersionManifest> FetchVersionManifestAsync();
    Task DownloadVersionAsync(MinecraftVersion version, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> IsVersionInstalledAsync(string versionId);
    Task<List<string>> GetInstalledVersionsAsync();
}

public class DownloadService : IDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly IUserConfigService _configService;
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string BmclapiMirror = "https://bmclapi2.bangbang93.com";

    public DownloadService(IUserConfigService configService)
    {
        _configService = configService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
    }

    public async Task<VersionManifest> FetchVersionManifestAsync()
    {
        try
        {
            var json = await _httpClient.GetStringAsync(VersionManifestUrl);
            var manifest = JsonSerializer.Deserialize<VersionManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (manifest != null)
            {
                var installed = await GetInstalledVersionsAsync();
                foreach (var v in manifest.Versions)
                {
                    v.IsInstalled = installed.Contains(v.Id);
                }
            }
            return manifest ?? new VersionManifest();
        }
        catch
        {
            return new VersionManifest();
        }
    }

    public Task<bool> IsVersionInstalledAsync(string versionId)
    {
        var gameDir = _configService.GetGameDirectory();
        var versionJsonPath = Path.Combine(gameDir, "versions", versionId, $"{versionId}.json");
        var versionJarPath = Path.Combine(gameDir, "versions", versionId, $"{versionId}.jar");
        return Task.FromResult(File.Exists(versionJsonPath) && File.Exists(versionJarPath));
    }

    public Task<List<string>> GetInstalledVersionsAsync()
    {
        var gameDir = _configService.GetGameDirectory();
        var versionsDir = Path.Combine(gameDir, "versions");
        var result = new List<string>();
        if (!Directory.Exists(versionsDir)) return Task.FromResult(result);

        foreach (var dir in Directory.GetDirectories(versionsDir))
        {
            var name = Path.GetFileName(dir);
            if (File.Exists(Path.Combine(dir, $"{name}.json")) &&
                File.Exists(Path.Combine(dir, $"{name}.jar")))
            {
                result.Add(name);
            }
        }
        return Task.FromResult(result);
    }

    public async Task DownloadVersionAsync(MinecraftVersion version, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var gameDir = _configService.GetGameDirectory();
        var versionDir = Path.Combine(gameDir, "versions", version.Id);
        Directory.CreateDirectory(versionDir);

        progress?.Report(0.05);

        var jsonPath = Path.Combine(versionDir, $"{version.Id}.json");
        await DownloadFileWithMirrorAsync(version.Url, jsonPath, cancellationToken);

        progress?.Report(0.15);

        var jsonContent = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        using var doc = JsonDocument.Parse(jsonContent);
        var downloads = doc.RootElement.GetProperty("downloads");

        string? clientUrl = null;
        if (downloads.TryGetProperty("client", out var client))
        {
            if (client.TryGetProperty("url", out var url))
                clientUrl = url.GetString();
        }

        if (string.IsNullOrEmpty(clientUrl))
            throw new InvalidOperationException("无法获取客户端下载地址");

        var jarPath = Path.Combine(versionDir, $"{version.Id}.jar");
        await DownloadFileWithMirrorAsync(clientUrl, jarPath, cancellationToken);

        progress?.Report(0.40);

        await DownloadLibrariesAsync(doc.RootElement, gameDir, versionDir, progress, 0.40, 0.85, cancellationToken);

        await DownloadAssetsAsync(doc.RootElement, gameDir, progress, 0.85, 0.99, cancellationToken);

        progress?.Report(1.0);
    }

    /// <summary>
    /// 下载 libraries（含 natives）。
    /// 会按当前系统过滤 rules、按系统下载并解压 natives 原生库。
    /// </summary>
    private async Task DownloadLibrariesAsync(JsonElement versionJson, string gameDir, string versionDir, IProgress<double>? progress, double startProgress, double endProgress, CancellationToken cancellationToken)
    {
        if (!versionJson.TryGetProperty("libraries", out var libraries)) return;

        var libsDir = Path.Combine(gameDir, "libraries");
        Directory.CreateDirectory(libsDir);

        var nativeDir = Path.Combine(versionDir, "natives");
        var nativeJars = new List<(string jarPath, string nativeDir, HashSet<string> exclude)>();

        var libList = new List<ResolvedLibrary>();
        foreach (var lib in libraries.EnumerateArray())
        {
            var resolved = ModVersion.ResolveLibrary(lib);
            if (resolved != null) libList.Add(resolved);
        }

        for (int i = 0; i < libList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lib = libList[i];
            var localPath = Path.Combine(libsDir, lib.Path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(localPath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    await DownloadFileWithMirrorAsync(lib.Url, localPath, cancellationToken);
                }
                catch { }
            }

            if (lib.IsNative)
            {
                nativeJars.Add((localPath, nativeDir, lib.ExcludeNames));
            }

            var p = startProgress + (endProgress - startProgress) * ((double)(i + 1) / Math.Max(1, libList.Count));
            progress?.Report(p);
        }

        // 解压 natives 原生库（不同系统文件不同，这里按当前系统提取）
        if (nativeJars.Count > 0)
        {
            ModNative.ClearNativeDir(nativeDir);
            foreach (var (jar, dir, exclude) in nativeJars)
            {
                try { ModNative.ExtractNativeJar(jar, dir, exclude); }
                catch { }
            }
        }
    }

    private async Task DownloadAssetsAsync(JsonElement versionJson, string gameDir, IProgress<double>? progress, double startProgress, double endProgress, CancellationToken cancellationToken)
    {
        if (!versionJson.TryGetProperty("assetIndex", out var assetIndex)) return;

        var assetsDir = Path.Combine(gameDir, "assets");
        Directory.CreateDirectory(assetsDir);

        var indexesDir = Path.Combine(assetsDir, "indexes");
        Directory.CreateDirectory(indexesDir);

        string? assetsId = null;
        string? indexUrl = null;

        if (assetIndex.TryGetProperty("id", out var id)) assetsId = id.GetString();
        if (assetIndex.TryGetProperty("url", out var url)) indexUrl = url.GetString();

        if (string.IsNullOrEmpty(assetsId) || string.IsNullOrEmpty(indexUrl)) return;

        var indexPath = Path.Combine(indexesDir, $"{assetsId}.json");
        await DownloadFileWithMirrorAsync(indexUrl, indexPath, cancellationToken);

        progress?.Report(startProgress + (endProgress - startProgress) * 0.1);

        var indexJson = await File.ReadAllTextAsync(indexPath, cancellationToken);
        using var doc = JsonDocument.Parse(indexJson);
        if (!doc.RootElement.TryGetProperty("objects", out var objects)) return;

        var objectsDir = Path.Combine(assetsDir, "objects");
        Directory.CreateDirectory(objectsDir);

        var objList = new List<(string hash, int count)>();
        foreach (var obj in objects.EnumerateObject())
        {
            if (obj.Value.TryGetProperty("hash", out var hash))
            {
                var h = hash.GetString() ?? "";
                if (h.Length >= 2)
                {
                    objList.Add((h, objList.Count + 1));
                }
            }
        }

        for (int i = 0; i < objList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (hash, _) = objList[i];
            var subHash = hash.Substring(0, 2);
            var dir = Path.Combine(objectsDir, subHash);
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, hash);

            if (!File.Exists(filePath))
            {
                try
                {
                    var resourceUrl = $"https://resources.download.minecraft.net/{subHash}/{hash}";
                    await DownloadFileWithMirrorAsync(resourceUrl, filePath, cancellationToken);
                }
                catch { }
            }

            var p = startProgress + (endProgress - startProgress) * (0.1 + 0.9 * ((double)(i + 1) / Math.Max(1, objList.Count)));
            progress?.Report(p);
        }
    }

    private async Task DownloadFileWithMirrorAsync(string url, string localPath, CancellationToken cancellationToken)
    {
        var bmclapiUrl = url;
        if (url.Contains("piston-data.mojang.com"))
            bmclapiUrl = url.Replace("https://piston-data.mojang.com", BmclapiMirror);
        else if (url.Contains("launchermeta.mojang.com"))
            bmclapiUrl = url.Replace("https://launchermeta.mojang.com", BmclapiMirror);
        else if (url.Contains("libraries.minecraft.net"))
            bmclapiUrl = url.Replace("https://libraries.minecraft.net", $"{BmclapiMirror}/libraries");
        else if (url.Contains("resources.download.minecraft.net"))
            bmclapiUrl = url.Replace("https://resources.download.minecraft.net", $"{BmclapiMirror}/assets");

        try
        {
            await DoDownloadFileAsync(bmclapiUrl, localPath, cancellationToken);
        }
        catch
        {
            await DoDownloadFileAsync(url, localPath, cancellationToken);
        }
    }

    private async Task DoDownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tempPath = localPath + ".tmp";
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await contentStream.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(cancellationToken);

        if (File.Exists(localPath)) File.Delete(localPath);
        File.Move(tempPath, localPath);
    }
}

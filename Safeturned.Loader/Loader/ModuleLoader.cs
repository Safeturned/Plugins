using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Safeturned.Loader.Config;
using Safeturned.Shared;
using UnityEngine;
using UnityEngine.Networking;
using SDG.Unturned;

namespace Safeturned.Loader;

public class ModuleLoader
{
    private readonly LoaderConfig _config;
    private bool _usedCustomInstaller;
    private const string CacheFileName = "metadata.json";
    private const string CacheSubdirectory = ".cache";
    private const string ModulesFolderName = "Modules";
    private const string InstallerZipName = "plugin.zip";
    private const string InstallerAssemblyName = "Safeturned.PluginInstaller.dll";

    private readonly string _loaderModuleDir;

    public ModuleLoader(LoaderConfig config)
    {
        _config = config;
        _loaderModuleDir = Path.Combine(ReadWrite.PATH, ModulesFolderName, "Safeturned.Loader");
    }

    public IEnumerator RunCoroutine()
    {
        if (!_config.Enabled)
        {
            LoaderLogger.Info("Loader is disabled; skipping installer startup.");
            yield break;
        }

        if (TryInstallFromCustomPath(out var customTarget))
        {
            LaunchPluginInstaller(customTarget);
            yield break;
        }

        LoaderMetadata metadata = null;
        yield return FetchLatestMetadata(result => metadata = result);

        if (metadata == null || string.IsNullOrWhiteSpace(metadata.DownloadUrl))
        {
            LoaderLogger.Info("No loader metadata found.");
            yield break;
        }

        Directory.CreateDirectory(_loaderModuleDir);
        var targetDir = Path.Combine(_loaderModuleDir, CacheSubdirectory, "installer");
        Directory.CreateDirectory(targetDir);

        var zipPath = Path.Combine(targetDir, InstallerZipName);
        var cache = LoadCache(targetDir);
        var packedVersion = metadata.PackedVersion != 0 ? metadata.PackedVersion : VersionHelper.PackVersion(metadata.Version);

        if (cache != null && cache.PackedVersion == packedVersion && File.Exists(zipPath))
        {
            LoaderLogger.Info("PluginInstaller already up-to-date ({0}), using cached zip.", metadata.Version);
        }
        else
        {
            var downloadOk = false;
            yield return DownloadFile(metadata.DownloadUrl, zipPath, success => downloadOk = success);
            if (!downloadOk)
            {
                if (File.Exists(zipPath))
                {
                    LoaderLogger.Error("Download failed, falling back to cached zip.");
                }
                else
                {
                    yield break;
                }
            }
        }

        if (!HashValidator.ValidateSha256(zipPath, metadata.Sha256))
        {
            LoaderLogger.Error("Hash validation failed. Removing downloaded file.");
            File.Delete(zipPath);
            yield break;
        }

        ZipHelper.ExtractZip(zipPath, targetDir);
        LoaderLogger.Info("Safeturned PluginInstaller updated to version {0}", metadata.Version);
        SaveCache(targetDir, metadata.Version, packedVersion);

        LaunchPluginInstaller(targetDir);

        if (_config.EnableUpdateShutdown && metadata != null)
        {
            yield return MonitorForSafeturnedUpdates(metadata);
        }
    }

    private IEnumerator FetchLatestMetadata(Action<LoaderMetadata> onComplete)
    {
        var baseUrl = $"{_config.ApiBaseUrl.TrimEnd('/')}/v1.0/plugin-installer";
        var url = string.IsNullOrWhiteSpace(_config.Version) || _config.Version.Equals("latest", StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl}?framework=module"
            : $"{baseUrl}?framework=module&version={_config.Version}";
        using var request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            LoaderLogger.Error("Failed to fetch loader metadata: {0}", request.error);
            onComplete(null);
            yield break;
        }

        var json = request.downloadHandler.text;
        onComplete(JsonConvert.DeserializeObject<LoaderMetadata>(json));
    }

    private IEnumerator DownloadFile(string url, string destination, Action<bool> onComplete)
    {
        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
        request.downloadHandler = new DownloadHandlerFile(destination);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            LoaderLogger.Error("Failed to download module zip: {0}", request.error);
            onComplete(false);
        }
        else
        {
            onComplete(true);
        }
    }

    private IEnumerator MonitorForSafeturnedUpdates(LoaderMetadata currentMetadata)
    {
        if (currentMetadata == null)
        {
            yield break;
        }

        var currentPackedVersion = currentMetadata.PackedVersion != 0
            ? currentMetadata.PackedVersion
            : VersionHelper.PackVersion(currentMetadata.Version);
        var requestUrl = $"{_config.ApiBaseUrl.TrimEnd('/')}/v1.0/plugin-installer?framework=module";
        LoaderLogger.Info("Monitoring Safeturned updates...");

        while (true)
        {
            yield return new WaitForSecondsRealtime(20f);
            using var request = UnityWebRequest.Get(requestUrl);
            request.redirectLimit = 5;
            request.SetRequestHeader("User-Agent", "Safeturned.Loader");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LoaderLogger.Error("Failed to check Safeturned updates: {0} (code {1})", request.error, request.responseCode);
                continue;
            }

            LoaderMetadata latest;
            try
            {
                latest = JsonConvert.DeserializeObject<LoaderMetadata>(request.downloadHandler.text);
            }
            catch (Exception ex)
            {
                LoaderLogger.Error("Failed to parse Safeturned update response: {0}", ex);
                continue;
            }

            if (latest == null)
            {
                LoaderLogger.Error("Safeturned update response was empty.");
                continue;
            }

            var newPackedVersion = latest.PackedVersion != 0
                ? latest.PackedVersion
                : VersionHelper.PackVersion(latest.Version);
            if (newPackedVersion == currentPackedVersion)
            {
                continue;
            }

            if (newPackedVersion > currentPackedVersion)
            {
                LoaderLogger.Warning("Detected newer Safeturned version: {0}", latest.Version);
            }
            else
            {
                LoaderLogger.Warning("Detected rollback to older Safeturned version: {0}", latest.Version);
            }

            if (_config.ForceUpdateShutdown is not true)
            {
                var warningTimes = new[]
                {
                    TimeSpan.FromMinutes(3),
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(1)
                };
                foreach (var timeLeft in warningTimes)
                {
                    ChatManager.say($"Server will shutdown in {timeLeft:g} for the Safeturned update.", ChatManager.welcomeColor);
                    yield return new WaitForSecondsRealtime((float)timeLeft.TotalSeconds);
                }
            }

            Provider.shutdown(0, "Safeturned is being updated");
            yield break;
        }
    }

    private bool TryInstallFromCustomPath(out string targetDir)
    {
        targetDir = Path.Combine(_loaderModuleDir, CacheSubdirectory, "installer");
        var customPath = _config.CustomInstallerPath;
        if (!_config.EnableCustomInstaller || string.IsNullOrWhiteSpace(customPath))
        {
            return false;
        }

        var fullPath = Path.IsPathRooted(customPath)
            ? customPath
            : Path.Combine(ReadWrite.PATH, customPath);
        if (Directory.Exists(targetDir))
        {
            try { Directory.Delete(targetDir, true); } catch { }
        }
        Directory.CreateDirectory(targetDir);

        if (Directory.Exists(fullPath))
        {
            LoaderLogger.Info("Using custom PluginInstaller from folder: {0}", fullPath);
            FileSystemHelper.CopyDirectory(fullPath, targetDir);
            _usedCustomInstaller = true;
            return true;
        }

        if (File.Exists(fullPath))
        {
            LoaderLogger.Info("Using custom PluginInstaller from file: {0}", fullPath);
            ZipHelper.ExtractZip(fullPath, targetDir);
            _usedCustomInstaller = true;
            return true;
        }

        LoaderLogger.Error("CustomInstallerPath not found: {0}", fullPath);
        return false;
    }

    private static CacheInfo LoadCache(string targetDir)
    {
        var path = Path.Combine(targetDir, CacheFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<CacheInfo>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(string targetDir, string version, uint packed)
    {
        var path = Path.Combine(targetDir, CacheFileName);
        var cache = new CacheInfo { Version = version, PackedVersion = packed };
        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(cache));
        }
        catch
        {
            // ignore cache write errors
        }
    }

    private void LaunchPluginInstaller(string targetDir)
    {
        try
        {
            var assemblyPath = Path.Combine(targetDir, InstallerAssemblyName);
            if (!File.Exists(assemblyPath))
            {
                LoaderLogger.Error("PluginInstaller assembly not found at {0}", assemblyPath);
                return;
            }
            var asm = Assembly.LoadFrom(assemblyPath);
            var runMethod = asm.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .FirstOrDefault(m => string.Equals(m.Name, "Run", StringComparison.Ordinal) && m.GetParameters().Length <= 1);
            if (runMethod == null)
            {
                LoaderLogger.Error("No static Run() entry point found in PluginInstaller.");
                return;
            }
            LoaderLogger.Info("Starting PluginInstaller...");
            var parametersInfo = runMethod.GetParameters();
            if (parametersInfo.Length == 0)
            {
                runMethod.Invoke(null, []);
            }
            else
            {
                string customModulePath = null;
                if (_usedCustomInstaller && !string.IsNullOrWhiteSpace(_config.CustomPluginPath))
                {
                    customModulePath = _config.CustomPluginPath;
                }
                var argsArray = customModulePath == null ? null : new object[] { customModulePath };
                runMethod.Invoke(null, [argsArray]);
            }
        }
        catch (Exception ex)
        {
            LoaderLogger.Error("Failed to launch PluginInstaller: {0}", ex);
        }
    }
}

public class LoaderMetadata
{
    public string Version { get; set; }
    public uint PackedVersion { get; set; }
    public string DownloadUrl { get; set; }
    public string Sha256 { get; set; }
}

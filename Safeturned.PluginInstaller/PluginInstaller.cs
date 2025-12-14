using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Safeturned.Shared;
using SDG.Framework.Modules;
using SDG.Unturned;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace Safeturned.PluginInstaller;

[UsedImplicitly]
public class PluginInstaller
{
    private readonly InstallerConfig _config;

    private const string CacheSubdirectory = ".cache";
    private const string CacheFileName = "metadata.json";
    private const string ModulesFolderName = "Modules";
    private const string PluginFolderName = "Safeturned.Module";

    public PluginInstaller(InstallerConfig config)
    {
        _config = config;
    }

    public IEnumerator RunCoroutine()
    {
        if (TryInstallFromCustomPath())
        {
            yield break;
        }

        InstallerMetadata metadata = null;
        yield return FetchMetadata(result => metadata = result);

        if (metadata == null || string.IsNullOrWhiteSpace(metadata.DownloadUrl))
        {
            InstallerLogger.Error("No plugin metadata found.");
            yield break;
        }

        var cacheDir = GetCacheDir();
        Directory.CreateDirectory(cacheDir);

        var moduleDir = GetModuleInstallDir();
        CleanDirectory(moduleDir);

        var zipPath = Path.Combine(cacheDir, "plugin.zip");
        var cache = LoadCache(cacheDir);
        var packed = metadata.PackedVersion != 0 ? metadata.PackedVersion : VersionHelper.PackVersion(metadata.Version);

        if (cache != null && cache.PackedVersion == packed && File.Exists(zipPath))
        {
            InstallerLogger.Info("Plugin already up-to-date ({0}), using cached zip.", metadata.Version);
        }
        else
        {
            var downloadSucceeded = false;
            yield return DownloadFile(metadata.DownloadUrl, zipPath, success => downloadSucceeded = success);

            if (!downloadSucceeded)
            {
                if (File.Exists(zipPath))
                {
                    InstallerLogger.Warning("Download failed, falling back to cached zip.");
                }
                else
                {
                    InstallerLogger.Error("Download failed and no cached zip available.");
                    yield break;
                }
            }
        }

        if (!HashValidator.ValidateSha256(zipPath, metadata.Sha256))
        {
            InstallerLogger.Error("Hash validation failed. Removing downloaded file.");
            File.Delete(zipPath);
            yield break;
        }

        ZipHelper.ExtractZip(zipPath, moduleDir);
        RenameModuleDlls(moduleDir);
        SaveCache(cacheDir, metadata.Version, packed);
        InstallerLogger.Info("Plugin installed version {0}", metadata.Version);

        ExportModuleMetadata(moduleDir, metadata.Version);
        RegisterModuleAssembly(moduleDir);
    }

    private IEnumerator FetchMetadata(Action<InstallerMetadata> onComplete)
    {
        var baseUrl = $"{_config.ApiBaseUrl.TrimEnd('/')}/v1.0/plugin-installer";
        var url = string.IsNullOrWhiteSpace(_config.Version) || _config.Version.Equals("latest", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}?version={_config.Version}";

        using var request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            InstallerLogger.Error("Failed to fetch plugin metadata: {0}", request.error);
            onComplete(null);
            yield break;
        }

        var json = request.downloadHandler.text;
        onComplete(JsonConvert.DeserializeObject<InstallerMetadata>(json));
    }

    private IEnumerator DownloadFile(string url, string destination, Action<bool> onComplete)
    {
        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
        request.downloadHandler = new DownloadHandlerFile(destination);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            InstallerLogger.Error("Failed to download plugin zip: {0}", request.error);
            onComplete(false);
        }
        else
        {
            onComplete(true);
        }
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
        }
    }

    [UsedImplicitly]
    public static void Run(object[] args = null)
    {
        InstallerLogger.Info("Loading Safeturned.PluginInstaller...");
        var config = CreateConfig(args);
        var installer = new PluginInstaller(config);
        var go = new GameObject("Safeturned.PluginInstaller");
        Object.DontDestroyOnLoad(go);
        var runner = go.AddComponent<CoroutineRunner>();
        runner.StartCoroutine(installer.RunCoroutine());
        InstallerLogger.Info("Safeturned.PluginInstaller started.");
    }

    private static InstallerConfig CreateConfig(object[] args)
    {
        var cfg = new InstallerConfig();
        if (args != null && args.Length > 0 && args[0] is string customPath && !string.IsNullOrWhiteSpace(customPath))
        {
            cfg.CustomModulePath = customPath;
            cfg.EnableCustomInstaller = true;
        }
        return cfg;
    }

    /// <summary>
    /// Installs from a custom local path (folder or zip) when EnableCustomInstaller is true.
    /// </summary>
    private bool TryInstallFromCustomPath()
    {
        if (!_config.EnableCustomInstaller || string.IsNullOrWhiteSpace(_config.CustomModulePath))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(_config.CustomModulePath);
        var moduleDir = GetModuleInstallDir();
        CleanDirectory(moduleDir);

        if (Directory.Exists(fullPath))
        {
            InstallerLogger.Info("Installing from custom folder: {0}", fullPath);
            FileSystemHelper.CopyDirectory(fullPath, moduleDir);
            RenameModuleDlls(moduleDir);
            InstallerLogger.Info("Plugin installed from custom folder.");
            ExportModuleMetadata(moduleDir, null);
            RegisterModuleAssembly(moduleDir);
            return true;
        }

        if (File.Exists(fullPath))
        {
            InstallerLogger.Info("Installing from custom zip: {0}", fullPath);
            ZipHelper.ExtractZip(fullPath, moduleDir);
            RenameModuleDlls(moduleDir);
            InstallerLogger.Info("Plugin installed from custom zip.");
            ExportModuleMetadata(moduleDir, null);
            RegisterModuleAssembly(moduleDir);
            return true;
        }

        InstallerLogger.Error("CustomModulePath not found: {0}", fullPath);
        return false;
    }

    private void RegisterModuleAssembly(string moduleDir)
    {
        if (string.IsNullOrWhiteSpace(moduleDir))
        {
            return;
        }

        // Look for renamed module file (without .dll extension to prevent auto-discovery)
        var assemblyPath = Path.Combine(moduleDir, "Safeturned.Module.bin");
        if (!File.Exists(assemblyPath))
        {
            InstallerLogger.Error("Safeturned.Module assembly not found at {0}", assemblyPath);
            return;
        }
        var loadedPlugin = Assembly.LoadFile(assemblyPath);
        var moduleType = loadedPlugin.GetTypes().FirstOrDefault(x => !x.IsAbstract && typeof(IModuleNexus).IsAssignableFrom(x));
        if (moduleType == null)
        {
            InstallerLogger.Error("Installer Module type is not found");
            return;
        }

        if (Activator.CreateInstance(moduleType) is not IModuleNexus plugin)
        {
            CommandWindow.LogError("Failed to create Module");
            return;
        }

        try
        {
            plugin.initialize();
            InstallerLogger.Info("Successfully registered Safeturned.Module assembly.");
        }
        catch (Exception ex)
        {
            InstallerLogger.Warning("Failed to register assembly: {0}", ex);
        }
    }

    /// <summary>
    /// Renames module DLLs to .bin extension to prevent Unturned from auto-discovering and loading them twice.
    /// The module descriptor will still reference these files explicitly.
    /// </summary>
    private void RenameModuleDlls(string moduleDir)
    {
        var moduleDll = Path.Combine(moduleDir, "Safeturned.Module.dll");
        if (File.Exists(moduleDll))
        {
            var newPath = Path.Combine(moduleDir, "Safeturned.Module.bin");
            File.Move(moduleDll, newPath);
            InstallerLogger.Info("Renamed module DLL to .bin to prevent duplicate loading");
        }
    }

    /// <summary>
    /// Copies module files to public Modules folder and fixes assembly paths in the descriptor.
    /// </summary>
    private void ExportModuleMetadata(string moduleDir, string version)
    {
        if (string.IsNullOrWhiteSpace(moduleDir))
        {
            return;
        }

        var modulesRoot = Path.Combine(ReadWrite.PATH, ModulesFolderName);
        var publicModuleDir = Path.Combine(modulesRoot, PluginFolderName);
        Directory.CreateDirectory(publicModuleDir);

        foreach (var fileName in new[] { "config.json", "English.dat", "Readme.txt" })
        {
            var sourcePath = Path.Combine(moduleDir, fileName);
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, Path.Combine(publicModuleDir, fileName), true);
            }
        }

        var descriptorPath = Path.Combine(moduleDir, $"{PluginFolderName}.module");
        if (!File.Exists(descriptorPath))
        {
            return;
        }

        try
        {
            var descriptorJson = File.ReadAllText(descriptorPath);
            var descriptor = JsonConvert.DeserializeObject<ModuleDescriptor>(descriptorJson) ?? new ModuleDescriptor();

            descriptor.Version = string.IsNullOrWhiteSpace(version) ? descriptor.Version : version;
            descriptor.Assemblies ??= [];

            foreach (var assembly in descriptor.Assemblies)
            {
                var assemblyFile = assembly.Path ?? string.Empty;
                assemblyFile = assemblyFile.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);

                // Check for renamed .bin file first (for Safeturned.Module.dll -> .bin)
                var fullAssemblyPath = Path.Combine(moduleDir, assemblyFile);
                if (!File.Exists(fullAssemblyPath) && assemblyFile.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    // Try .bin extension
                    var binPath = Path.ChangeExtension(fullAssemblyPath, ".bin");
                    if (File.Exists(binPath))
                    {
                        fullAssemblyPath = binPath;
                    }
                }

                if (!File.Exists(fullAssemblyPath))
                {
                    continue;
                }

                var relative = FileSystemHelper.GetRelativePathFallback(publicModuleDir, fullAssemblyPath)
                    .Replace(Path.DirectorySeparatorChar, '/');

                assembly.Path = relative;
            }

            var destination = Path.Combine(publicModuleDir, $"{PluginFolderName}.module");
            File.WriteAllText(destination, JsonConvert.SerializeObject(descriptor, Formatting.Indented));
        }
        catch (Exception ex)
        {
            InstallerLogger.Warning("Failed to export module descriptor: {0}", ex);
        }
    }

    private void CleanDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                InstallerLogger.Warning("Failed to clean directory {0}: {1}", path, ex.Message);
            }
        }

        Directory.CreateDirectory(path);
    }

    private string GetPluginInstallerDir()
    {
        var loaderDir = Path.Combine(ReadWrite.PATH, ModulesFolderName, "Safeturned.Loader");
        return Path.Combine(loaderDir, CacheSubdirectory, "installer");
    }

    private string GetCacheDir()
    {
        var loaderDir = Path.Combine(ReadWrite.PATH, ModulesFolderName, "Safeturned.Loader");
        return Path.Combine(loaderDir, CacheSubdirectory, "installer");
    }

    private string GetModuleInstallDir()
    {
        var loaderDir = Path.Combine(ReadWrite.PATH, ModulesFolderName, "Safeturned.Loader");
        return Path.Combine(loaderDir, CacheSubdirectory, "module");
    }

    private class ModuleDescriptor
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public List<ModuleAssemblyEntry> Assemblies { get; set; }
    }

    private class ModuleAssemblyEntry
    {
        public string Path { get; set; }
        public string Role { get; set; }
    }
}

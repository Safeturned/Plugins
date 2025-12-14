using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Safeturned.Loader.Config;
using Safeturned.Shared;
using SDG.Framework.Modules;
using SDG.Unturned;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Safeturned.Loader;

[UsedImplicitly]
public class Module : IModuleNexus
{
    private ModuleLoader _loader;
    private CoroutineRunner _coroutineRunner;

    public void initialize()
    {
        var assemblyName = typeof(Module).Assembly.GetName();
        LoaderLogger.Info("Loading Safeturned.Loader {0}...", assemblyName.Version);
        var config = LoadConfig();
        _loader = new ModuleLoader(config);
        var go = new GameObject();
        Object.DontDestroyOnLoad(go);
        _coroutineRunner = go.AddComponent<CoroutineRunner>();
        _coroutineRunner.StartCoroutine(_loader.RunCoroutine());
        LoaderLogger.Info("Safeturned.Loader initialized.");
    }

    public void shutdown()
    {
        LoaderLogger.Info("Safeturned.Loader shutting down.");
        if (_coroutineRunner != null)
        {
            Object.Destroy(_coroutineRunner.gameObject);
        }
    }

    private static LoaderConfig LoadConfig()
    {
        const string fileName = "config.json";
        string resolvedDir = null;

        // Prefer directory of the loaded assembly.
        var assemblyLocation = typeof(Module).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyLocation))
        {
            var dir = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                resolvedDir = dir;
            }
        }

        // Fallback: scan Modules for the loader dll (handles nested folders).
        if (resolvedDir == null)
        {
            var modulesDirectory = Path.Combine(ReadWrite.PATH, "Modules");
            var dllPath = Directory.GetFiles(modulesDirectory, "Safeturned.Loader.dll", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(dllPath))
            {
                resolvedDir = Path.GetDirectoryName(dllPath);
            }
        }

        var configPath = resolvedDir != null ? Path.Combine(resolvedDir, fileName) : fileName;
        if (!File.Exists(configPath))
        {
            LoaderLogger.Error("Config file not found: {0}", configPath);
            return new LoaderConfig();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonConvert.DeserializeObject<LoaderConfig>(json);
            return config ?? new LoaderConfig();
        }
        catch (Exception ex)
        {
            LoaderLogger.Error("Failed to load config: {0}", ex.Message);
            return new LoaderConfig();
        }
    }
}

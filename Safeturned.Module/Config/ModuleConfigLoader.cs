using System;
using System.IO;
using Newtonsoft.Json;

namespace Safeturned.Module.Config;

public static class ModuleConfigLoader
{
    private const string DefaultFileName = "config.json";

    public static ModuleConfig Load(string path = null)
    {
        var filePath = string.IsNullOrWhiteSpace(path) ? DefaultFileName : path;

        if (!File.Exists(filePath))
        {
            ModuleLogger.Error("Config file not found: {0}", filePath);
            return new ModuleConfig();
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var config = JsonConvert.DeserializeObject<ModuleConfig>(json) ?? new ModuleConfig();
            ModuleLogger.Info("Loaded config from {0}", filePath);
            return config;
        }
        catch (Exception ex)
        {
            ModuleLogger.Error("Failed to load config {0}: {1}", filePath, ex.Message);
            return new ModuleConfig();
        }
    }
}

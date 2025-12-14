using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Safeturned.Module.Commands;
using Safeturned.Module.Config;
using Safeturned.Module.ExceptionReporting;
using Safeturned.Shared;
using SDG.Framework.Modules;
using SDG.Unturned;
using UnityEngine;
using UnityEngine.Networking;
using ModuleConfig = Safeturned.Module.Config.ModuleConfig;
using ModuleConfigLoader = Safeturned.Module.Config.ModuleConfigLoader;
using Object = UnityEngine.Object;

namespace Safeturned.Module;

[UsedImplicitly]
public class ModuleEntry : IModuleNexus
{
    private GameObject _runnerObject;
    private Coroutine _scanCoroutine;
    private ModuleRunner _runner;
    internal CoroutineRunner _coroutineRunner;

    public void initialize()
    {
        var asm = typeof(ModuleEntry).Assembly;
        var assemblyLocation = asm.Location;
        var configDir = Path.GetDirectoryName(assemblyLocation) ?? string.Empty;
        PluginInfo.Init(FindAssemblyVersion("Safeturned.Loader"), FindAssemblyVersion("Safeturned.PluginInstaller"), asm.GetName().Version?.ToString(), assemblyLocation);
        ModuleLogger.Info("Safeturned Version: {0}", PluginInfo.ModuleVersion);
        _runnerObject = new GameObject();
        Object.DontDestroyOnLoad(_runnerObject);
        _coroutineRunner = _runnerObject.AddComponent<CoroutineRunner>();
        _scanCoroutine = _coroutineRunner.StartCoroutine(InitAndRun(_coroutineRunner));
    }

    public void shutdown()
    {
        if (_runnerObject != null)
        {
            Object.Destroy(_runnerObject);
        }
    }

    private IEnumerator InitAndRun(CoroutineRunner runner)
    {
        var configPath = Path.Combine(PluginInfo.BaseDirectory, "config.json");
        var config = ModuleConfigLoader.Load(configPath);
        if (string.IsNullOrWhiteSpace(config.ApiKey) || config.ApiKey == "sk_live_or_test_key_here")
        {
            ModuleLogger.Error("==========================================================");
            ModuleLogger.Error("ERROR: API key is missing in config.json");
            ModuleLogger.Error("Please add your API key to continue.");
            ModuleLogger.Error("==========================================================");
            yield break;
        }

        yield return FetchAndMergeRemoteConfig(config);

        ExceptionReporter.Init(config);
        _runner = new ModuleRunner(config);
        _scanCoroutine = runner.StartCoroutine(_runner.ScanLoop());
        RegisterCommands(_runner, runner);
        runner.StartCoroutine(ExceptionReporter.FlushQueue());
        ModuleLogger.Info("Safeturned is now protecting your server!");
        ModuleLogger.Info("Use '/safeturned help' for available commands.");
    }

    private IEnumerator FetchAndMergeRemoteConfig(ModuleConfig config)
    {
        var url = $"{config.ApiBaseUrl.TrimEnd('/')}/v1.0/frameworks/module/config";
        using var request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            ModuleLogger.Error("Failed to fetch remote config: {0}", request.error);
            yield break;
        }

        try
        {
            var json = request.downloadHandler.text;
            var remote = JsonUtility.FromJson<RemoteConfig>(json);
            if (remote != null)
            {
                MergeLists(config.WatchPaths, remote.watchPaths);
                MergeLists(config.IncludePatterns, remote.includePatterns);
                MergeLists(config.ExcludePatterns, remote.excludePatterns);
                ModuleLogger.Info("Merged remote config defaults.");
            }
        }
        catch
        {
            ModuleLogger.Error("Failed to parse remote config.");
        }
    }

    private static void MergeLists(List<string> target, IEnumerable<string> source)
    {
        if (source == null)
            return;
        foreach (var item in source)
        {
            var trimmed = item?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            if (!target.Any(x => string.Equals(x?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
                target.Add(trimmed);
        }
    }

    [Serializable]
    private class RemoteConfig
    {
        public List<string> watchPaths = [];
        public List<string> includePatterns = [];
        public List<string> excludePatterns = [];
    }

    private void RegisterCommands(ModuleRunner runner, CoroutineRunner runnerHost)
    {
        try
        {
            RegisterCommand(new CommandSafeturned(runner, runnerHost));
            RegisterCommand(new CommandSafeturnedAlias(runner, runnerHost));
        }
        catch (Exception ex)
        {
            ModuleLogger.Error("Failed to register commands: {0}", ex);
            ExceptionReporter.Report(ex, "command_register_fail");
        }
    }

    private static void RegisterCommand(CommandSafeturnedBase command)
    {
        Commander.register(command);

        if (Commander.commands.FirstOrDefault(x => x.GetType() == command.GetType()) == null)
        {
            throw new InvalidOperationException($"Failed to register \"{command.command}\" command");
        }
    }

    private static string FindAssemblyVersion(string namePrefix)
    {
        var asm = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a =>
            {
                var n = a.GetName().Name;
                return n != null && n.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase);
            });
        return asm?.GetName().Version?.ToString();
    }
}

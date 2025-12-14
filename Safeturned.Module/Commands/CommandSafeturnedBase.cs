using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Safeturned.Module.Config;
using Safeturned.Module.ExceptionReporting;
using Safeturned.Shared;
using SDG.Unturned;
using Steamworks;
using UnityEngine.Networking;

namespace Safeturned.Module.Commands;

public abstract class CommandSafeturnedBase : Command
{
    private readonly ModuleRunner _runner;
    private readonly CoroutineRunner _coroutineRunner;

    protected CommandSafeturnedBase(string commandName, ModuleRunner runner, CoroutineRunner coroutineRunner)
    {
        localization = new Local();
        _command = commandName;
        _info = "Safeturned controls";
        _help = "/safeturned status|rescan [path]|cache|config|version";
        _runner = runner;
        _coroutineRunner = coroutineRunner;
    }

    protected override void execute(CSteamID executorID, string parameter)
    {
        try
        {
            var arguments = Parser.getComponentsFromSerial(parameter, '/');
            var action = arguments.Length > 0 ? arguments[0].ToLowerInvariant() : "status";
            switch (action)
            {
                case "status": case "s":
                    PrintStatus();
                    break;
                case "rescan":
                    var targets = arguments.Length > 1 ? new List<string> { string.Join(" ", arguments.Skip(1)) } : null;
                    _coroutineRunner.StartCoroutine(_runner.ScanOnce(targets));
                    if (targets != null)
                        ModuleLogger.Info("Scanning path: {0}", targets[0]);
                    else
                        ModuleLogger.Info("Scanning all configured paths...");
                    break;
                case "cache":
                    _runner.ClearCaches();
                    ModuleLogger.Info("Cache cleared successfully.");
                    break;
                case "config":
                    PrintConfig();
                    break;
                case "version": case "v":
                    PrintVersion();
                    _coroutineRunner.StartCoroutine(CheckForUpdates());
                    break;
                case "help":
                    PrintHelp();
                    break;
                default:
                    PrintHelp();
                    break;
            }
        }
        catch (Exception ex)
        {
            ModuleLogger.Error("Command failed: {0}", ex);
            ExceptionReporter.Report(ex, "command_" + _command, _runner.GetRateLimitState(), _runner.GetConfig());
        }
    }

    private void PrintHelp()
    {
        ModuleLogger.Info("=== Safeturned Commands ===");
        ModuleLogger.Info("/safeturned status - Show current status and API usage");
        ModuleLogger.Info("/safeturned version - Show installed versions and check for updates");
        ModuleLogger.Info("/safeturned config - Display current configuration");
        ModuleLogger.Info("/safeturned rescan [path] - Manually scan for new/changed files");
        ModuleLogger.Info("/safeturned cache - Clear the file hash cache");
        ModuleLogger.Info("Shortcut: /st can be used instead of /safeturned");
    }

    private void PrintStatus()
    {
        var state = _runner.GetRateLimitState();

        ModuleLogger.Info("=== Safeturned Status ===");
        ModuleLogger.Info("Status: Running");

        if (state.Limit <= 0)
        {
            ModuleLogger.Info("API Tokens: Unlimited (no rate limit configured)");
        }
        else
        {
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(state.ResetUnixSeconds).ToLocalTime();
            ModuleLogger.Info("API Tokens: {0}/{1} (resets at {2:HH:mm:ss})", state.BucketTokens, state.Limit, resetTime);
        }
    }

    private void PrintConfig()
    {
        var cfg = _runner.GetConfig();
        ModuleLogger.Info("=== Safeturned Configuration ===");
        ModuleLogger.Info("Scan Interval: {0} seconds", cfg.ScanIntervalSeconds);
        ModuleLogger.Info("Max Concurrent Uploads: {0}", cfg.MaxConcurrentUploads);
        ModuleLogger.Info("Force Analyze: {0}", cfg.ForceAnalyze ? "Yes" : "No");
        ModuleLogger.Info("Watch Paths: {0}", string.Join(", ", (cfg.WatchPaths ?? []).Distinct()));
        ModuleLogger.Info("Include Patterns: {0}", string.Join(", ", (cfg.IncludePatterns ?? []).Distinct()));
        if (cfg.ExcludePatterns != null && cfg.ExcludePatterns.Count > 0)
            ModuleLogger.Info("Exclude Patterns: {0}", string.Join(", ", cfg.ExcludePatterns.Distinct()));
    }

    private void PrintVersion()
    {
        ModuleLogger.Info("=== Safeturned Versions ===");
        ModuleLogger.Info("Loader: {0}", PluginInfo.LoaderVersion);
        ModuleLogger.Info("Plugin Installer: {0}", PluginInfo.PluginInstallerVersion);
        ModuleLogger.Info("Module: {0}", PluginInfo.ModuleVersion);
    }

    private IEnumerator CheckForUpdates()
    {
        var config = _runner.GetConfig();
        var url = $"{config.ApiBaseUrl.TrimEnd('/')}/v1.0/plugin-installer?framework=module";

        using var request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            ModuleLogger.Info("Could not check for updates (API unavailable)");
            yield break;
        }

        try
        {
            var json = request.downloadHandler.text;
            var metadata = JsonConvert.DeserializeObject<VersionMetadata>(json);
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.Version))
            {
                yield break;
            }
            if (metadata.PackedVersion > PluginInfo.PackedModuleVersion)
            {
                ModuleLogger.Info("*** UPDATE AVAILABLE ***");
                ModuleLogger.Info("New version {0} is available!", metadata.Version);
                ModuleLogger.Info("Please restart your server to update.");
            }
            else if (metadata.PackedVersion == PluginInfo.PackedModuleVersion)
            {
                ModuleLogger.Info("You are running the latest version!");
            }
        }
        catch
        {
            // Silently ignore parse errors
        }
    }

    private class VersionMetadata
    {
        public string Version { get; set; }
        public uint PackedVersion { get; set; }
    }
}

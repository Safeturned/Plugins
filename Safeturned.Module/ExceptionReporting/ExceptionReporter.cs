using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Safeturned.Module.Config;
using Safeturned.Module.RateLimiting;
using UnityEngine.Networking;

namespace Safeturned.Module.ExceptionReporting;

public static class ExceptionReporter
{
    private static ModuleConfig _config;
    private static string _queuePath;
    private static bool _initialized;

    public static void Init(ModuleConfig config)
    {
        _config = config;
        _queuePath = Path.Combine(PluginInfo.BaseDirectory, "safeturned.exceptions.json");
        _initialized = true;
    }

    public static void Report(Exception ex, string context, RateLimitState state = null, ModuleConfig cfg = null)
    {
        if (!_initialized || _config == null || !_config.ReportErrors)
        {
            return;
        }

        try
        {
            var reports = LoadQueue();
            var report = BuildReport(ex, context, state, cfg ?? _config);
            reports.Add(report);
            TrimQueue(reports);
            SaveQueue(reports);
        }
        catch
        {
            // swallow reporting errors
        }
    }

    public static IEnumerator FlushQueue()
    {
        if (!_initialized || _config == null || !_config.ReportErrors || string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            yield break;
        }

        List<ExceptionReport> reports;
        try
        {
            reports = LoadQueue();
        }
        catch
        {
            yield break;
        }

        if (reports.Count == 0)
        {
            yield break;
        }

        var url = $"{_config.ApiBaseUrl.TrimEnd('/')}/v1.0/exception";
        List<ExceptionReport> remaining = [];
        foreach (var report in reports)
        {
            var json = JsonConvert.SerializeObject(report);
            var body = Encoding.UTF8.GetBytes(json);
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-API-Key", _config.ApiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                remaining.Add(report);
            }
        }

        SaveQueue(remaining);
    }

    private static ExceptionReport BuildReport(Exception ex, string context, RateLimitState state, ModuleConfig cfg)
    {
        var stack = ex.ToString();
        const int max = 4000;
        if (stack.Length > max)
        {
            stack = stack.Substring(0, max);
        }

        state ??= new RateLimitState();

        return new ExceptionReport
        {
            ModuleVersion = PluginInfo.ModuleVersion,
            LoaderVersion = PluginInfo.LoaderVersion,
            InstallerVersion = PluginInfo.PluginInstallerVersion,
            FrameworkName = "module",
            Message = ex.Message,
            Type = ex.GetType().FullName ?? "Exception",
            StackTrace = stack,
            WatchPaths = cfg.WatchPaths ?? [],
            IncludePatterns = cfg.IncludePatterns ?? [],
            ExcludePatterns = cfg.ExcludePatterns ?? [],
            ForceAnalyze = cfg.ForceAnalyze,
            MaxConcurrentUploads = cfg.MaxConcurrentUploads,
            RateLimitTokens = state.BucketTokens,
            RateLimitLimit = state.Limit,
            RateLimitReset = state.ResetUnixSeconds,
            Context = context,
            OccurredAtUtc = DateTime.UtcNow
        };
    }

    private static List<ExceptionReport> LoadQueue()
    {
        if (string.IsNullOrWhiteSpace(_queuePath) || !File.Exists(_queuePath))
        {
            return [];
        }

        var json = File.ReadAllText(_queuePath);
        var list = JsonConvert.DeserializeObject<List<ExceptionReport>>(json);
        return list ?? [];
    }

    private static void SaveQueue(List<ExceptionReport> reports)
    {
        if (string.IsNullOrWhiteSpace(_queuePath))
        {
            return;
        }

        var json = JsonConvert.SerializeObject(reports, Formatting.None);
        File.WriteAllText(_queuePath, json);
    }

    private static void TrimQueue(List<ExceptionReport> reports, int max = 10)
    {
        if (reports.Count <= max)
        {
            return;
        }

        reports.RemoveRange(0, reports.Count - max);
    }
}

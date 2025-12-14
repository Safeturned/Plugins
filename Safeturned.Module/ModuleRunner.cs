using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Safeturned.Module.Config;
using Safeturned.Module.ExceptionReporting;
using Safeturned.Module.RateLimiting;
using Safeturned.Module.Scanning;
using Safeturned.Module.Uploader;
using SDG.Unturned;
using UnityEngine;

namespace Safeturned.Module;

public class ModuleRunner
{
    private readonly ModuleConfig _config;
    private readonly FileScanner _scanner;
    private readonly FileHashCache _hashCache;
    private readonly UploadClient _uploader;
    private readonly RateLimitCache _bucketCache;
    private readonly RateLimitBucket _bucket;
    private readonly string _baseDirectory;
    private readonly PendingUploadQueue _pendingQueue;

    public ModuleRunner(ModuleConfig config)
    {
        _config = config;

        _baseDirectory = PluginInfo.BaseDirectory;
        Directory.CreateDirectory(_baseDirectory);

        _hashCache = new FileHashCache(Path.Combine(_baseDirectory, "hashes.json"));
        _scanner = new FileScanner(_hashCache);

        _bucketCache = new RateLimitCache(Path.Combine(_baseDirectory, "ratelimit.json"));
        _bucket = new RateLimitBucket();
        var saved = _bucketCache.Load();
        _bucket.SeedFromHeaders(saved.Limit, saved.BucketTokens, saved.ResetUnixSeconds);

        _pendingQueue = new PendingUploadQueue(Path.Combine(_baseDirectory, "pending.json"));

        _uploader = new UploadClient(_config.ApiBaseUrl, _config.ApiKey, _bucket);
    }

    public IEnumerator ScanLoop()
    {
        ModuleLogger.Info("Auto-scan enabled: checking every {0} seconds", _config.ScanIntervalSeconds);
        var firstScan = true;
        while (true)
        {
            if (firstScan)
            {
                ModuleLogger.Info("Running initial scan...");
                firstScan = false;
            }
            yield return SafeRunCoroutine(ScanIteration(), "scan_loop");
            yield return new WaitForSeconds(_config.ScanIntervalSeconds);
        }
    }

    public IEnumerator ScanOnce(IEnumerable<string> specificPaths = null)
    {
        var roots = specificPaths?.ToList();
        roots = roots != null && roots.Count > 0 ? roots : _config.WatchPaths;
        yield return SafeRunCoroutine(ScanIteration(roots), "rescan");
    }

    public void ClearCaches()
    {
        _hashCache.Clear();
        _bucketCache.Clear();
    }

    public RateLimitState GetRateLimitState() => _bucket.State;

    public ModuleConfig GetConfig() => _config;

    private IEnumerator SafeRunCoroutine(IEnumerator inner, string context)
    {
        while (true)
        {
            bool moveNext;
            object current = null;
            try
            {
                moveNext = inner.MoveNext();
                if (moveNext)
                {
                    current = inner.Current;
                }
            }
            catch (Exception ex)
            {
                ModuleLogger.Error("Error in {0}: {1}", context, ex);
                ExceptionReporter.Report(ex, context, _bucket.State, _config);
                yield break;
            }

            if (!moveNext)
            {
                yield break;
            }

            yield return current;
        }
    }

    private IEnumerator ScanIteration(IEnumerable<string> rootsOverride = null)
    {
        var roots = rootsOverride ?? _config.WatchPaths;
        var expandedRoots = ExpandWildcardPaths(roots);

        Queue<UploadTask> pendingUploads = new();
        List<UploadTask> activeUploads = [];
        var uploadResults = new List<bool>();

        var retryCount = _pendingQueue.DrainTo(pendingUploads);
        if (retryCount > 0)
            ModuleLogger.Info("Retrying {0} previously failed upload(s)", retryCount);

        var scannedPaths = 0;
        var skippedPaths = 0;
        foreach (var root in expandedRoots)
        {
            var resolvedRoot = ResolvePathToServerRoot(root);
            if (!Directory.Exists(resolvedRoot))
            {
                skippedPaths++;
                continue;
            }

            scannedPaths++;
            var changes = _scanner.EnumerateChanged(resolvedRoot, _config.IncludePatterns, _config.ExcludePatterns);
            var fileCount = 0;
            foreach (var (path, hash) in changes)
            {
                fileCount++;
                pendingUploads.Enqueue(new UploadTask(path, Path.GetFileName(path), _config.ForceAnalyze, hash));
            }
            if (fileCount > 0)
                ModuleLogger.Info("Queued {0} file(s) for upload from: {1}", fileCount, resolvedRoot);
        }

        while (pendingUploads.Count > 0 || activeUploads.Count > 0)
        {
            while (activeUploads.Count < _config.MaxConcurrentUploads && pendingUploads.Count > 0)
            {
                var task = pendingUploads.Dequeue();
                task.Coroutine = _uploader.UploadFile(task.Path, task.FileName, task.ForceAnalyze, result =>
                {
                    task.Completed = true;
                    task.Success = result;
                });
                activeUploads.Add(task);
            }

            foreach (var task in activeUploads)
            {
                if (!task.Completed)
                    task.Coroutine.MoveNext();
            }

            for (var i = activeUploads.Count - 1; i >= 0; i--)
            {
                var task = activeUploads[i];
                if (task.Completed)
                {
                    uploadResults.Add(task.Success);
                    if (!task.Success)
                        _pendingQueue.Enqueue(task.Path, task.FileName, task.ForceAnalyze);
                    else
                        _hashCache.MarkUploaded(task.Path, task.Hash);
                    activeUploads.RemoveAt(i);
                }
            }

            yield return null;
        }

        var successCount = uploadResults.Count(r => r);
        var failedCount = uploadResults.Count - successCount;

        if (successCount > 0 && failedCount > 0)
        {
            ModuleLogger.Info("Scan complete: {0} file(s) uploaded successfully, {1} failed", successCount, failedCount);
        }
        else if (successCount > 0)
        {
            ModuleLogger.Info("Scan complete: {0} file(s) uploaded successfully", successCount);
        }
        else if (failedCount > 0)
        {
            ModuleLogger.Info("Scan complete: all {0} upload(s) failed", failedCount);
        }
        else if (scannedPaths > 0)
        {
            ModuleLogger.Info("Scan complete: no new or changed files found in {0} path(s)", scannedPaths);
        }
        else if (skippedPaths > 0)
        {
            ModuleLogger.Info("Scan complete: all {0} configured path(s) not found", skippedPaths);
        }

        _bucketCache.Save(_bucket.State);
    }

    /// <summary>
    /// Expands wildcard patterns in paths. Supports '*' to match any directory name.
    /// Example: "Servers/*/Rocket" expands to ["Servers/Server1/Rocket", "Servers/Server2/Rocket", ...]
    /// </summary>
    private List<string> ExpandWildcardPaths(IEnumerable<string> paths)
    {
        List<string> result = [];
        foreach (var path in paths)
        {
            if (!path.Contains('*'))
            {
                result.Add(path);
                continue;
            }
            var expanded = ExpandSingleWildcardPath(path);
            result.AddRange(expanded);
        }
        return result;
    }

    private List<string> ExpandSingleWildcardPath(string pattern)
    {
        var parts = pattern.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        List<string> currentPaths = [""];
        foreach (var part in parts)
        {
            List<string> nextPaths = [];
            foreach (var currentPath in currentPaths)
            {
                if (part == "*")
                {
                    var searchDir = string.IsNullOrEmpty(currentPath) ? "." : currentPath;
                    var resolvedSearchDir = ResolvePathToServerRoot(searchDir);
                    if (!Directory.Exists(resolvedSearchDir))
                    {
                        continue;
                    }
                    try
                    {
                        var dirs = Directory.GetDirectories(resolvedSearchDir);
                        // Convert back to relative paths for consistency
                        var relativeDirs = dirs.Select(d => Path.IsPathRooted(d) && d.StartsWith(ReadWrite.PATH)
                            ? d.Substring(ReadWrite.PATH.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            : d).ToArray();
                        nextPaths.AddRange(relativeDirs);
                    }
                    catch
                    {
                        // Skip if can't read directory
                    }
                }
                else
                {
                    var nextPath = string.IsNullOrEmpty(currentPath) ? part : Path.Combine(currentPath, part);
                    nextPaths.Add(nextPath);
                }
            }
            currentPaths = nextPaths;
        }
        return currentPaths;
    }

    /// <summary>
    /// Resolves a path relative to the Unturned server root directory.
    /// Absolute paths are returned as-is, relative paths are combined with ReadWrite.PATH.
    /// </summary>
    private string ResolvePathToServerRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ReadWrite.PATH;
        }

        // If already absolute, use as-is
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        // Combine with server root for relative paths
        return Path.Combine(ReadWrite.PATH, path);
    }
}

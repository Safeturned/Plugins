using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Safeturned.Module.Uploader;

internal class PendingUpload
{
    public string Path { get; set; }
    public string FileName { get; set; }
    public bool ForceAnalyze { get; set; }
    public long SizeBytes { get; set; }
}

internal class PendingUploadQueue
{
    private readonly string _filePath;
    private readonly int _maxItems;
    private readonly long _maxTotalBytes;
    private readonly List<PendingUpload> _queue;
    private readonly object _lock = new();

    public PendingUploadQueue(string filePath, int maxItems = 100, long maxTotalBytes = 50 * 1024 * 1024)
    {
        _filePath = filePath;
        _maxItems = Math.Max(1, maxItems);
        _maxTotalBytes = Math.Max(1024 * 1024, maxTotalBytes);
        _queue = Load();
    }

    public bool Enqueue(string path, string fileName, bool forceAnalyze)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch
        {
            return false;
        }

        lock (_lock)
        {
            if (_queue.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _queue.Add(new PendingUpload
            {
                Path = path,
                FileName = fileName,
                ForceAnalyze = forceAnalyze,
                SizeBytes = size
            });

            TrimIfNeeded();
            Save();
        }

        return true;
    }

    public int DrainTo(Queue<UploadTask> destination)
    {
        lock (_lock)
        {
            var count = 0;
            while (_queue.Count > 0)
            {
                var next = _queue[0];
                _queue.RemoveAt(0);
                if (File.Exists(next.Path))
                {
                    destination.Enqueue(new UploadTask(next.Path, next.FileName, next.ForceAnalyze, null));
                    count++;
                }
            }
            if (count > 0)
                Save();
            return count;
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    private void TrimIfNeeded()
    {
        while (_queue.Count > _maxItems || TotalBytes() > _maxTotalBytes)
        {
            _queue.RemoveAt(0);
        }
    }

    private long TotalBytes()
    {
        long total = 0;
        foreach (var item in _queue)
        {
            total += item.SizeBytes;
            if (total > _maxTotalBytes)
            {
                break;
            }
        }
        return total;
    }

    private List<PendingUpload> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonConvert.DeserializeObject<List<PendingUpload>>(json);
                if (list != null)
                {
                    return list;
                }
            }
        }
        catch
        {
            // ignore load errors
        }

        return [];
    }

    private void Save()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_queue, Formatting.None);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // ignore save errors
        }
    }
}

internal class UploadTask
{
    public string Path { get; }
    public string FileName { get; }
    public bool ForceAnalyze { get; }
    public string Hash { get; }
    public System.Collections.IEnumerator Coroutine { get; set; }
    public bool Completed { get; set; }
    public bool Success { get; set; }

    public UploadTask(string path, string fileName, bool forceAnalyze, string hash)
    {
        Path = path;
        FileName = fileName;
        ForceAnalyze = forceAnalyze;
        Hash = hash;
    }
}

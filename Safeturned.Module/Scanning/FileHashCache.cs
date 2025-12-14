using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Safeturned.Module.Scanning;

public class FileHashCache
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private Dictionary<string, string> _cache;

    public FileHashCache(string filePath)
    {
        _filePath = filePath;
        _cache = Load();
    }

    public bool HasChanged(string path, string hash)
    {
        lock (_lock)
        {
            return !_cache.TryGetValue(path, out var previous) ||
                   !string.Equals(previous, hash, StringComparison.Ordinal);
        }
    }

    public void MarkUploaded(string path, string hash)
    {
        lock (_lock)
        {
            _cache[path] = hash;
            Save();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
    }

    private Dictionary<string, string> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                return dict != null
                    ? new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_cache, Formatting.None);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Silently ignore save errors to prevent crashing scan loop
        }
    }
}

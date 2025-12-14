using System.IO;
using Newtonsoft.Json;

namespace Safeturned.Module.RateLimiting;

public class RateLimitCache
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public RateLimitCache(string filePath)
    {
        _filePath = filePath;
    }

    public RateLimitState Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
            {
                ModuleLogger.Info("Rate limit cache not found, starting fresh");
                return new RateLimitState();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var state = JsonConvert.DeserializeObject<RateLimitState>(json) ?? new RateLimitState();
                ModuleLogger.Info("Loaded rate limit cache");
                return state;
            }
            catch
            {
                ModuleLogger.Error("Failed to load rate limit cache, starting fresh");
                return new RateLimitState();
            }
        }
    }

    public void Save(RateLimitState state)
    {
        lock (_lock)
        {
            try
            {
                var json = JsonConvert.SerializeObject(state, Formatting.None);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Silently ignore cache save errors
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
                ModuleLogger.Info("Cleared rate limit cache");
            }
        }
    }
}

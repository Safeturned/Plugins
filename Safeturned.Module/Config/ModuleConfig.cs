using System.Collections.Generic;

namespace Safeturned.Module.Config;

public class ModuleConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.safeturned.com";
    public int ScanIntervalSeconds { get; set; } = 300;
    public List<string> WatchPaths { get; set; } =
    [
        "Modules",
        "Servers/*/Rocket/Plugins",
        "Servers/*/OpenMod/plugins"
    ];
    public List<string> IncludePatterns { get; set; } = ["*.dll"];
    public List<string> ExcludePatterns { get; set; } = [];
    public bool ForceAnalyze { get; set; } = false;
    public int MaxConcurrentUploads { get; set; } = 3;
    public string LogLevel { get; set; } = "Info";
    public string UpdateChannel { get; set; } = "Release";
    public bool ReportErrors { get; set; } = true;
}

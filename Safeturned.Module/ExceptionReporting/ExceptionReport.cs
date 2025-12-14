using System;
using System.Collections.Generic;

namespace Safeturned.Module.ExceptionReporting;

public class ExceptionReport
{
    public string ModuleVersion { get; set; }
    public string LoaderVersion { get; set; }
    public string InstallerVersion { get; set; }
    public string FrameworkName { get; set; }
    public string Message { get; set; }
    public string Type { get; set; }
    public string StackTrace { get; set; }
    public List<string> WatchPaths { get; set; }
    public List<string> IncludePatterns { get; set; }
    public List<string> ExcludePatterns { get; set; }
    public bool ForceAnalyze { get; set; }
    public int MaxConcurrentUploads { get; set; }
    public int RateLimitTokens { get; set; }
    public int RateLimitLimit { get; set; }
    public long RateLimitReset { get; set; }
    public string Context { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

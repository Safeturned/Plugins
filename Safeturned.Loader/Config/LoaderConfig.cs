namespace Safeturned.Loader.Config;

public class LoaderConfig
{
    public string ApiBaseUrl { get; set; } = "https://api.safeturned.com";
    public string Configuration { get; set; } = "Release";
    /// <summary>Enable the loader to start the PluginInstaller (and module install) on startup.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Optional: pin a specific version. Use "latest" (default) to auto-update.</summary>
    public string Version { get; set; } = "latest";
    /// <summary>Set true to enable using CustomInstallerPath; when false, API flow is used.</summary>
    public bool EnableCustomInstaller { get; set; }
    /// <summary>Optional: use a local PluginInstaller instead of downloading. Absolute or relative path to folder or zip.</summary>
    public string CustomInstallerPath { get; set; } = "SafeturnedBuild/Safeturned.PluginInstaller";
    /// <summary>Optional: point at a local Safeturned.Module folder or zip for the custom installer.</summary>
    public string CustomPluginPath { get; set; } = "SafeturnedBuild/Safeturned.Module";
    /// <summary>Enable checking for plugin updates and shutting down the server when a new version is detected.</summary>
    public bool EnableUpdateShutdown { get; set; }
    /// <summary>Skip countdown warnings and immediately shutdown when an update is detected.</summary>
    public bool ForceUpdateShutdown { get; set; }
}

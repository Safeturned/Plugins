namespace Safeturned.PluginInstaller;

public class InstallerConfig
{
    public string ApiBaseUrl { get; set; } = "https://api.safeturned.com";
    public string Version { get; set; } = "latest";

    /// <summary>
    /// Path to local folder or zip. Used when EnableCustomInstaller is true for testing builds without uploading to API.
    /// </summary>
    public string CustomModulePath { get; set; }

    public bool EnableCustomInstaller { get; set; }
}

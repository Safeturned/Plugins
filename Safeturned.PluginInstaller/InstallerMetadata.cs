namespace Safeturned.PluginInstaller;

public class InstallerMetadata
{
    public string Version { get; set; }
    public uint PackedVersion { get; set; }
    public string DownloadUrl { get; set; }

    /// <summary>
    /// SHA256 hash in Base64 format for integrity validation.
    /// </summary>
    public string Sha256 { get; set; }
}

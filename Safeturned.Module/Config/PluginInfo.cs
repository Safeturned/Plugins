using System.IO;
using Safeturned.Shared;

namespace Safeturned.Module.Config;

public static class PluginInfo
{
    public static void Init(string loaderVersion, string pluginInstallerVersion, string moduleVersion, string location)
    {
        LoaderVersion = ToSemver(loaderVersion);
        PluginInstallerVersion = ToSemver(pluginInstallerVersion);
        ModuleVersion = ToSemver(moduleVersion);
        PackedModuleVersion = VersionHelper.PackVersion(ModuleVersion);
        Location = location;
        BaseDirectory = Path.GetDirectoryName(location) ?? ".";
    }

    public static string LoaderVersion { get; private set; }
    public static string PluginInstallerVersion { get; private set; }
    public static string ModuleVersion { get; private set; }
    public static uint PackedModuleVersion { get; private set; }
    public static string Location { get; private set; }
    public static string BaseDirectory { get; private set; }

    private static string ToSemver(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var parts = version.Split('.');
        if (parts.Length < 3)
            return version;

        return $"{parts[0]}.{parts[1]}.{parts[2]}";
    }
}
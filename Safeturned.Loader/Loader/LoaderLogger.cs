using SDG.Unturned;

namespace Safeturned.Loader;

public static class LoaderLogger
{
    private const string Prefix = "[Safeturned.Loader]";

    public static void Info(string message, params object[] args)
    {
        CommandWindow.Log($"{Prefix}[INF] {string.Format(message, args)}");
    }

    public static void Warning(string message, params object[] args)
    {
        CommandWindow.LogWarning($"{Prefix}[WRN] {string.Format(message, args)}");
    }

    public static void Error(string message, params object[] args)
    {
        CommandWindow.LogError($"{Prefix}[ERR] {string.Format(message, args)}");
    }
}

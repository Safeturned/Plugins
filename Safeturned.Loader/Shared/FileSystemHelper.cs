using System;
using System.IO;

namespace Safeturned.Shared;

public static class FileSystemHelper
{
    public static void CopyDirectory(string sourceDir, string destinationDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = GetRelativePathFallback(sourceDir, file);
            var destFile = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile) ?? destinationDir);
            File.Copy(file, destFile, true);
        }
    }

    public static string GetRelativePathFallback(string baseDir, string fullPath)
    {
        var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(baseDir)));
        var fullUri = new Uri(Path.GetFullPath(fullPath));
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
    }

    public static string AppendDirectorySeparatorChar(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            return path + Path.DirectorySeparatorChar;
        }

        return path;
    }
}

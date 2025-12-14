using System.IO;
using System.IO.Compression;

namespace Safeturned.Shared;

public static class ZipHelper
{
    public static void ExtractZip(string zipPath, string targetDir)
    {
        using (var stream = File.OpenRead(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }
                var destinationPath = Path.Combine(targetDir, entry.FullName);
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                entry.ExtractToFile(destinationPath, true);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Safeturned.Module.Scanning;

public class FileScanner
{
    private readonly FileHashCache _cache;

    public FileScanner(FileHashCache cache)
    {
        _cache = cache;
    }

    public IEnumerable<(string path, string hash)> EnumerateChanged(string rootPath, IEnumerable<string> includePatterns, IEnumerable<string> excludePatterns)
    {
        var include = includePatterns.ToList();
        var exclude = excludePatterns.ToList();

        List<(string, string)> results = [];
        if (!Directory.Exists(rootPath))
        {
            return results;
        }

        var files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => MatchesInclude(f, include))
            .Where(f => !MatchesExclude(f, exclude));

        foreach (var file in files)
        {
            var hash = FileHasher.ComputeHash(file);
            if (_cache.HasChanged(file, hash))
            {
                results.Add((file, hash));
            }
        }

        return results;
    }

    private static bool MatchesInclude(string path, IEnumerable<string> patterns)
    {
        if (!patterns.Any())
        {
            return true;
        }
        return patterns.Any(p => GlobMatch(path, p));
    }

    private static bool MatchesExclude(string path, IEnumerable<string> patterns)
    {
        return patterns.Any(p => GlobMatch(path, p));
    }

    private static bool GlobMatch(string path, string pattern)
    {
        var fileName = Path.GetFileName(path);
        if (pattern == "*")
            return true;
        if (pattern.StartsWith("*."))
        {
            var ext = pattern.Substring(1);
            return fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        }
        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}

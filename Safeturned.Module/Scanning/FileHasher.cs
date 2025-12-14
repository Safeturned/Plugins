using System;
using System.IO;
using System.Security.Cryptography;

namespace Safeturned.Module.Scanning;

public static class FileHasher
{
    public static string ComputeHash(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToBase64String(hash);
    }
}

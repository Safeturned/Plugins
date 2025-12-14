using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Safeturned.Shared;

public static class HashValidator
{
    public static bool ValidateSha256(string filePath, string expectedBase64)
    {
        if (string.IsNullOrWhiteSpace(expectedBase64) || !File.Exists(filePath))
        {
            return true; // If no hash provided, skip validation.
        }

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(expectedBase64);
        }
        catch
        {
            return true;
        }

        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(filePath))
        {
            var actual = sha.ComputeHash(stream);
            return actual.SequenceEqual(expected);
        }
    }
}

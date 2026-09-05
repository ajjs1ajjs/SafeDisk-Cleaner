using System.Security.Cryptography;

namespace SafeDiskCleaner.Core.Utils;

/// <summary>SHA-256 helpers used by the self-update integrity checks.</summary>
public static class Sha256Checksum
{
    /// <summary>Computes the lowercase hex SHA-256 of a file (streamed).</summary>
    public static string ComputeFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// Extracts a SHA-256 hex digest from raw text: either a bare 64-char hex
    /// string or the first token of a <c>sha256sum</c>-style line
    /// ("&lt;hex&gt;  filename", optional leading '*' marker).
    /// Returns null when no valid digest is present.
    /// </summary>
    public static string? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var token = content.Trim()
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .TrimStart('*')
            .Trim('"', '\'');

        if (token is not { Length: 64 })
        {
            return null;
        }

        foreach (var ch in token)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return null;
            }
        }

        return token.ToLowerInvariant();
    }
}

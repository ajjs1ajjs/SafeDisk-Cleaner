using System.Text;
using System.Text.RegularExpressions;

namespace SafeDiskCleaner.Core.Safety;

/// <summary>
/// Matches filesystem paths against user-defined exclusion rules.
/// A pattern without wildcards is a prefix match (covers the directory and
/// everything inside it); <c>*</c> and <c>?</c> wildcards are supported.
/// Matching is case-insensitive and separator-agnostic (/ vs \).
/// </summary>
public static class PathExclusions
{
    /// <summary>Returns true when <paramref name="path"/> matches any pattern.</summary>
    public static bool IsExcluded(string path, IReadOnlyList<string> patterns)
    {
        if (patterns is not { Count: > 0 } || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = Normalize(path);
        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var pattern = Normalize(raw.TrimEnd('/', '\\'));
            if (pattern.IndexOfAny(['*', '?']) >= 0)
            {
                if (Regex.IsMatch(normalized, WildcardToRegex(pattern), RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            else if (IsPrefixMatch(normalized, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPrefixMatch(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // boundary: exact match or a directory separator right after,
        // so "C:\Temp" must not exclude "C:\Temporary"
        return path.Length == prefix.Length ||
               path[prefix.Length] is '/' or '\\';
    }

    private static string Normalize(string value) => value.Trim().Replace('/', '\\');

    internal static string WildcardToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (var ch in pattern)
        {
            switch (ch)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                default: sb.Append(Regex.Escape(ch.ToString())); break;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }
}

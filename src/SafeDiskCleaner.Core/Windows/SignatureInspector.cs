using System.Security.Cryptography.X509Certificates;

namespace SafeDiskCleaner.Core.Windows;

/// <summary>
/// Authenticode signature inspection for Microsoft-signed binaries.
///
/// SECURITY (SEC-001): the previous implementation spawned a PowerShell process
/// per uncached file, passing the path through an environment variable. Any
/// attacker-influenced scan path reached a shell-adjacent API. This version
/// performs no process spawn and no shell invocation: the signer certificate
/// is extracted in-process via <see cref="X509CertificateLoader"/> and only
/// its Subject/Issuer strings are inspected. Any failure (unsigned file,
/// unreadable file, non-PE content, non-Windows OS) safely returns false.
/// </summary>
public sealed class SignatureInspector
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public bool HasMicrosoftSignature(string path)
    {
        // Signatures do not change for a given file during a run; caching avoids
        // re-parsing the certificate per candidate.
        return _cache.GetOrAdd(path, static (p, @this) => @this.Check(p), this);
    }

    private bool Check(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            // NOTE: CreateFromSignedFile is flagged SYSLIB0057 (obsolete) with no
            // signed-file equivalent on X509CertificateLoader in .NET 10. It is
            // used here deliberately: in-process Authenticode signer extraction
            // with zero process spawn / shell surface (SEC-001). Revisit when the
            // runtime ships a supported replacement.
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var subject = cert.Subject?.ToLowerInvariant() ?? string.Empty;
            var issuer = cert.Issuer?.ToLowerInvariant() ?? string.Empty;

            return subject.Contains("microsoft") || subject.Contains("windows")
                || issuer.Contains("microsoft") || issuer.Contains("windows");
        }
        catch
        {
            // Unsigned, unreadable, non-PE, or removed concurrently — not Microsoft-signed.
            return false;
        }
    }
}

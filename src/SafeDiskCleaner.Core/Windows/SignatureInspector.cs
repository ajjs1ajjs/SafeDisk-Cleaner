using System.Diagnostics;

namespace SafeDiskCleaner.Core.Windows;

/// <summary>
/// Authenticode signature inspection for Microsoft-signed binaries.
///
/// SECURITY: the file path is passed to PowerShell through an environment
/// variable — never interpolated into the command line. This eliminates the
/// command-injection vector present in the original Rust implementation
/// (which embedded the path into a single-quoted PowerShell string).
/// A hard timeout prevents a hung PowerShell from blocking the caller.
/// </summary>
public sealed class SignatureInspector
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const string Script =
        "$sig = Get-AuthenticodeSignature -LiteralPath $env:SDC_SIGNATURE_PATH; " +
        "if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject }";

    public bool HasMicrosoftSignature(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + Script + "\"",
            };
            startInfo.EnvironmentVariables["SDC_SIGNATURE_PATH"] = path;

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best-effort cleanup
                }

                return false;
            }

            if (process.ExitCode != 0)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            return output.Contains("microsoft", StringComparison.OrdinalIgnoreCase)
                || output.Contains("windows", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

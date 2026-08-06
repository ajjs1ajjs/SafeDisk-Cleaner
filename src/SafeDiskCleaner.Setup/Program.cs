using System.Diagnostics;
using System.Runtime.InteropServices;

const uint MB_ICONINFORMATION = 0x40;
const uint MB_SETFOREGROUND = 0x10000;
const uint MB_SYSTEMMODAL = 0x1000;
const string Version = "0.3.0";

var msiPath = ExtractMsi();
var exitCode = 1;

try
{
    var startInfo = new ProcessStartInfo("msiexec.exe")
    {
        Arguments = $"/i \"{msiPath}\" /passive",
        UseShellExecute = true,
    };

    using (var process = Process.Start(startInfo))
    {
        process?.WaitForExit();
        exitCode = process?.ExitCode ?? 1;
    }

    var message = exitCode == 0
        ? $"SafeDisk Cleaner v{Version} успішно встановлено."
        : $"Встановлення завершилося з кодом {exitCode}.";

    ShowMessage(message, "SafeDisk Cleaner — інсталятор");
}
catch (Exception ex)
{
    ShowMessage($"Не вдалося запустити інсталятор: {ex.Message}", "SafeDisk Cleaner — інсталятор");
}
finally
{
    try
    {
        File.Delete(msiPath);
    }
    catch
    {
        // best-effort cleanup
    }
}

return exitCode;

static string ExtractMsi()
{
    var assembly = typeof(Program).Assembly;
    using var stream = assembly.GetManifestResourceStream("SafeDiskCleaner.msi")
        ?? throw new InvalidOperationException("Installer payload not found");

    var directory = Path.Combine(Path.GetTempPath(), "SafeDiskSetup");
    Directory.CreateDirectory(directory);

    var path = Path.Combine(directory, $"SafeDiskCleaner-{Guid.NewGuid():N}.msi");
    using (var file = File.Create(path))
    {
        stream.CopyTo(file);
    }

    return path;
}

static void ShowMessage(string text, string title)
{
    _ = MessageBoxW(IntPtr.Zero, text, title, MB_ICONINFORMATION | MB_SETFOREGROUND | MB_SYSTEMMODAL);
}

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

using System.Text;
using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Core.Platform;

/// <summary>
/// Trash implementation for Unix desktops. Linux follows the freedesktop.org
/// Trash specification (XDG_DATA_HOME/Trash with files/ and info/).
/// macOS uses ~/.Trash (Finder), which needs no .trashinfo sidecar.
/// </summary>
public sealed class UnixRecycleBin : IRecycleBin
{
    private string FilesDir =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trash", "files");

    /// <summary>
    /// Ensures the trash directory exists, is a real directory (not a
    /// planted symlink) and is owner-only. A symlinked Trash would redirect
    /// trashed (possibly sensitive) content anywhere.
    /// </summary>
    private static void EnsureTrashDir(string dir)
    {
        var info = new DirectoryInfo(dir);
        if (info.LinkTarget is not null)
        {
            throw new IOException($"Trash directory is a symlink: {dir}");
        }
        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch
            {
                // best-effort hardening only
            }
        }
    }

    private string InfoDir =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trash", "info");

    private bool IsMacTrash => OperatingSystem.IsMacOS();

    public RecycleBinInfo? Query(string? root = null)
    {
        try
        {
            if (!Directory.Exists(FilesDir))
            {
                return null;
            }

            ulong size = 0;
            ulong count = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(FilesDir))
            {
                try
                {
                    if (File.Exists(entry))
                    {
                        size = SaturatingAdd(size, (ulong)new FileInfo(entry).Length);
                        count++;
                    }
                    else if (Directory.Exists(entry))
                    {
                        size = SaturatingAdd(size, DirSize(entry));
                        count++;
                    }
                }
                catch
                {
                    // removed concurrently
                }
            }

            return count == 0 && size == 0 ? null : new RecycleBinInfo(size, count);
        }
        catch
        {
            return null;
        }
    }

    public bool Empty()
    {
        ulong failed = 0;
        try
        {
            if (Directory.Exists(FilesDir))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(FilesDir))
                {
                    try
                    {
                        if (File.Exists(entry))
                        {
                            File.Delete(entry);
                        }
                        else if (Directory.Exists(entry))
                        {
                            Directory.Delete(entry, recursive: true);
                        }
                    }
                    catch
                    {
                        // skip in-use entries, keep going — but count them
                        failed++;
                    }
                }
            }

            // On macOS FilesDir == InfoDir, already emptied above.
            if (!IsMacTrash && Directory.Exists(InfoDir))
            {
                foreach (var file in Directory.EnumerateFiles(InfoDir))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }

            return failed == 0;
        }
        catch
        {
            return false;
        }
    }

    public void MoveToRecycleBin(string path)
    {
        if (!File.Exists(path))
        {
            throw new IOException($"File not found: {path}");
        }

        Directory.CreateDirectory(FilesDir);
        EnsureTrashDir(FilesDir);
        if (!IsMacTrash)
        {
            Directory.CreateDirectory(InfoDir);
            EnsureTrashDir(InfoDir);
        }

        var name = Path.GetFileName(path);
        var target = name;
        var n = 1;
        while (File.Exists(Path.Combine(FilesDir, target)) || Directory.Exists(Path.Combine(FilesDir, target)))
        {
            target = $"{name}.{n++}";
        }

        var targetPath = Path.Combine(FilesDir, target);
        try
        {
            File.Move(path, targetPath);
        }
        catch (IOException)
        {
            File.Copy(path, targetPath);
            File.Delete(path);
        }

        if (!IsMacTrash)
        {
            var infoPath = Path.Combine(InfoDir, target + ".trashinfo");
            var info = new StringBuilder()
                .AppendLine("[Trash Info]")
                .Append("Path=").AppendLine(EncodePath(path))
                .Append("DeletionDate=").AppendLine(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"))
                .ToString();
            File.WriteAllText(infoPath, info, Encoding.UTF8);
        }
    }

    private static ulong DirSize(string dir)
    {
        ulong total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total = SaturatingAdd(total, (ulong)new FileInfo(file).Length);
                }
                catch
                {
                    // removed concurrently
                }
            }
        }
        catch
        {
            // unreadable dir counts as 0
        }

        return total;
    }

    private static ulong SaturatingAdd(ulong a, ulong b)
    {
        try
        {
            return checked(a + b);
        }
        catch (OverflowException)
        {
            return ulong.MaxValue;
        }
    }

    private static string EncodePath(string path)
    {
        // The spec requires percent-encoding; slashes are preserved.
        return Uri.EscapeDataString(path).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
    }
}

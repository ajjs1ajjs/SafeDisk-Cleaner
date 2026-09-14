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
                        size += (ulong)new FileInfo(entry).Length;
                        count++;
                    }
                    else if (Directory.Exists(entry))
                    {
                        size += DirSize(entry);
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
                        // skip in-use entries, keep going
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
                        // skip, keep going
                    }
                }
            }

            return true;
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
        if (!IsMacTrash)
        {
            Directory.CreateDirectory(InfoDir);
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
                    total += (ulong)new FileInfo(file).Length;
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

    private static string EncodePath(string path)
    {
        // The spec requires percent-encoding; slashes are preserved.
        return Uri.EscapeDataString(path).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
    }
}

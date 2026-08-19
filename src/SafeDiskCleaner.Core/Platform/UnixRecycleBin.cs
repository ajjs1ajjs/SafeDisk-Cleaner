using System.Text;
using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Core.Platform;

/// <summary>
/// Trash implementation for Unix desktops following the freedesktop.org
/// Trash specification (XDG_DATA_HOME/Trash with files/ and info/).
/// </summary>
public sealed class UnixRecycleBin : IRecycleBin
{
    private string FilesDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trash", "files");

    private string InfoDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trash", "info");

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
            foreach (var file in Directory.EnumerateFiles(FilesDir))
            {
                try
                {
                    size += (ulong)new FileInfo(file).Length;
                    count++;
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
                foreach (var file in Directory.EnumerateFiles(FilesDir))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // skip in-use entries, keep going
                    }
                }
            }

            if (Directory.Exists(InfoDir))
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
        Directory.CreateDirectory(InfoDir);

        var name = Path.GetFileName(path);
        var target = name;
        var n = 1;
        while (File.Exists(Path.Combine(FilesDir, target)))
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

        var infoPath = Path.Combine(InfoDir, target + ".trashinfo");
        var info = new StringBuilder()
            .AppendLine("[Trash Info]")
            .Append("Path=").AppendLine(EncodePath(path))
            .Append("DeletionDate=").AppendLine(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"))
            .ToString();
        File.WriteAllText(infoPath, info, Encoding.UTF8);
    }

    private static string EncodePath(string path)
    {
        // The spec requires percent-encoding; slashes are preserved.
        return Uri.EscapeDataString(path).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
    }
}
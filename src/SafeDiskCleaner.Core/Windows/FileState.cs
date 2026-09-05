namespace SafeDiskCleaner.Core.Windows;

public static class FileState
{
    /// <summary>
    /// Returns the raw Win32 attributes of a file (0 when it cannot be read).
    /// </summary>
    public static uint GetAttributes(string path)
    {
        try
        {
            return (uint)File.GetAttributes(path);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Best-effort check whether a file is open/locked by another process.
    /// A file that opens exclusively for write is free; one that only opens for
    /// read is considered locked by a writer.
    /// </summary>
    public static bool IsLocked(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            try
            {
                using var __ = File.OpenRead(path);
                return true;
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool HasSystemAttribute(string path) =>
        WindowsApi.IsSystemAttribute(GetAttributes(path));
}

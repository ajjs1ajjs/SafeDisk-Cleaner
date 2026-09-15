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
    /// A file that opens exclusively is free. A sharing violation means
    /// somebody holds the file: locked-by-writer (read-only open succeeds)
    /// or exclusively held (even a read open fails) — both count as locked.
    /// A file that vanished concurrently counts as NOT locked (the validator
    /// denies vanished files through its own fail-closed metadata gates).
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
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
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
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch
            {
                // Even a read-only open is refused: the file is held
                // exclusively by another process — definitely locked.
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool HasSystemAttribute(string path) =>
        WindowsApi.IsSystemAttribute(GetAttributes(path));

    /// <summary>
    /// True when attributes could not be read at all (ACL-denied or worse).
    /// The validator fails closed on this; <see cref="HasSystemAttribute"/>
    /// alone cannot distinguish "no flag" from "unreadable".
    /// </summary>
    public static bool AttributesUnreadable(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>
    /// True when the lock probe itself was inconclusive (ACL-denied open).
    /// An unreadable file must not look "free to delete".
    /// </summary>
    public static bool LockCheckInconclusive(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}

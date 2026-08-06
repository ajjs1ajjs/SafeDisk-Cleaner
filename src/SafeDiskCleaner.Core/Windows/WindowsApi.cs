using System.Runtime.InteropServices;
using SafeDiskCleaner.Core.Models;
using DriveInfo = SafeDiskCleaner.Core.Models.DriveInfo;

namespace SafeDiskCleaner.Core.Windows;

internal static class NativeMethods
{
    public const int DRIVE_NO_ROOT_DIR = 1;
    public const int DRIVE_REMOVABLE = 2;
    public const int DRIVE_FIXED = 3;
    public const int DRIVE_REMOTE = 4;
    public const int DRIVE_CDROM = 5;
    public const int DRIVE_RAMDISK = 6;

    public const uint FILE_ATTRIBUTE_SYSTEM = 0x4;

    public const uint FO_DELETE = 0x0003;
    public const ushort FOF_ALLOWUNDO = 0x0040;
    public const ushort FOF_NOCONFIRMATION = 0x0010;
    public const ushort FOF_SILENT = 0x0004;
    public const ushort FOF_NOERRORUI = 0x0400;

    public const uint SHERB_NOCONFIRMATION = 0x1;
    public const uint SHERB_NOPROGRESSUI = 0x2;
    public const uint SHERB_NOSOUND = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHQUERYRBINFO
    {
        public uint cbSize;
        public ulong i64Size;
        public ulong i64NumItems;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetLogicalDrives();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetDriveTypeW([MarshalAs(UnmanagedType.LPWStr)] string rootPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetDiskFreeSpaceExW(
        [MarshalAs(UnmanagedType.LPWStr)] string directoryName,
        out ulong freeBytesAvailableToCaller,
        out ulong totalNumberOfBytes,
        out ulong totalNumberOfFreeBytes);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHQueryRecycleBinW(
        [MarshalAs(UnmanagedType.LPWStr)] string? pszRootPath,
        ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHEmptyRecycleBinW(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszRootPath,
        uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);
}

public static class WindowsApi
{
    public static string DriveTypeName(uint type) => type switch
    {
        NativeMethods.DRIVE_FIXED => "fixed",
        NativeMethods.DRIVE_REMOVABLE => "removable",
        NativeMethods.DRIVE_REMOTE => "remote",
        NativeMethods.DRIVE_CDROM => "cdrom",
        NativeMethods.DRIVE_RAMDISK => "ramdisk",
        NativeMethods.DRIVE_NO_ROOT_DIR => "no_root",
        _ => "unknown",
    };

    public static bool IsSystemAttribute(uint attributes) =>
        (attributes & NativeMethods.FILE_ATTRIBUTE_SYSTEM) != 0;

    public static IReadOnlyList<DriveInfo> ListDrives()
    {
        var result = new List<DriveInfo>();
        var mask = NativeMethods.GetLogicalDrives();
        if (mask == 0)
        {
            return result;
        }

        for (var i = 0; i < 26; i++)
        {
            if ((mask & (1u << i)) == 0)
            {
                continue;
            }

            var letter = (char)('A' + i);
            var root = $"{letter}:\\";
            var kind = DriveTypeName(NativeMethods.GetDriveTypeW(root));
            if (NativeMethods.GetDiskFreeSpaceExW(root, out var free, out var total, out var totalFree) && total > 0)
            {
                result.Add(new DriveInfo
                {
                    Letter = $"{letter}:",
                    Kind = kind,
                    Total = total,
                    Free = free,
                    Used = total - totalFree,
                });
            }
        }

        return result;
    }

    public static RecycleBinInfo? QueryRecycleBin(string? root = null)
    {
        var info = new NativeMethods.SHQUERYRBINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>(),
        };
        var hr = NativeMethods.SHQueryRecycleBinW(root, ref info);
        return hr == 0
            ? new RecycleBinInfo(info.i64Size, info.i64NumItems)
            : null;
    }

    public static bool EmptyRecycleBin()
    {
        var hr = NativeMethods.SHEmptyRecycleBinW(
            IntPtr.Zero,
            null,
            NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);
        return hr == 0;
    }

    public static void MoveToRecycleBin(string path)
    {
        var op = new NativeMethods.SHFILEOPSTRUCT
        {
            wFunc = NativeMethods.FO_DELETE,
            pFrom = path + '\0',
            pTo = IntPtr.Zero,
            fFlags = (ushort)(NativeMethods.FOF_ALLOWUNDO
                | NativeMethods.FOF_NOCONFIRMATION
                | NativeMethods.FOF_SILENT
                | NativeMethods.FOF_NOERRORUI),
        };
        var ret = NativeMethods.SHFileOperationW(ref op);
        if (ret != 0 || op.fAnyOperationsAborted != 0)
        {
            throw new IOException($"Failed to move to recycle bin (code {ret})");
        }
    }
}

public readonly record struct RecycleBinInfo(ulong Size, ulong Count);

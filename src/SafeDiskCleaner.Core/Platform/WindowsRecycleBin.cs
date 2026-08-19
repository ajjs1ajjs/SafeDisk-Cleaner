using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Core.Platform;

/// <summary>Recycle Bin access via the Win32 shell APIs.</summary>
public sealed class WindowsRecycleBin : IRecycleBin
{
    public RecycleBinInfo? Query(string? root = null) => WindowsApi.QueryRecycleBin(root);

    public bool Empty() => WindowsApi.EmptyRecycleBin();

    public void MoveToRecycleBin(string path) => WindowsApi.MoveToRecycleBin(path);
}
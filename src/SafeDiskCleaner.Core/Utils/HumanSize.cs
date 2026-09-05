using System.Globalization;

namespace SafeDiskCleaner.Core.Utils;

public static class HumanSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return "?";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value.ToString(unit >= 3 ? "0.00" : "0.0", CultureInfo.InvariantCulture)} {Units[unit]}";
    }
}

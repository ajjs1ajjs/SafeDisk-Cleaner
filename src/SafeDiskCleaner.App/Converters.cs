using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Utils;

namespace SafeDiskCleaner.App;

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DriveIsSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        var letter = values[0] as string;
        var roots = values[1] as string;
        if (string.IsNullOrEmpty(letter) || string.IsNullOrEmpty(roots)) return false;
        var rootsList = roots.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var root = $"{letter}\\";
        return rootsList.Any(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RiskToBrushConverter : IValueConverter
{
    private static readonly Brush Safe = new SolidColorBrush(Color.FromRgb(0x34, 0xC0, 0x8A));
    private static readonly Brush Medium = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x4A));
    private static readonly Brush Advanced = new SolidColorBrush(Color.FromRgb(0xE0, 0x5B, 0x5B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RiskLevel level
            ? level switch
            {
                RiskLevel.Safe => Safe,
                RiskLevel.Medium => Medium,
                _ => Advanced,
            }
            : Brushes.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RiskToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RiskLevel level ? level.Label() : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ConfidenceToBrushConverter : IValueConverter
{
    private static readonly Brush High = new SolidColorBrush(Color.FromRgb(0x34, 0xC0, 0x8A));
    private static readonly Brush Mid = new SolidColorBrush(Color.FromRgb(0x3A, 0xA2, 0xFF));
    private static readonly Brush Low = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x4A));
    private static readonly Brush Keep = new SolidColorBrush(Color.FromRgb(0xE0, 0x5B, 0x5B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is byte confidence
            ? confidence switch
            {
                >= 95 => High,
                >= 80 => Mid,
                >= 50 => Low,
                _ => Keep,
            }
            : Brushes.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ActionToCheckEnabledConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CandidateAction action && action != CandidateAction.Keep;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BytesToHumanSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            long l => HumanSize.Format(l),
            ulong ul => HumanSize.Format((long)ul),
            int i => HumanSize.Format(i),
            null => "—",
            _ => "—",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

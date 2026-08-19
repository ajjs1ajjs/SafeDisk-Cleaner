using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Utils;

namespace SafeDiskCleaner.App;

public sealed class IconGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            "ViewDashboardOutline" => "◈",
            "MagnifyScan" => "⌕",
            "ContentCopy" => "⧉",
            "ShieldLock" => "☰",
            "TextBoxSearch" => "▤",
            "CogOutline" => "⚙",
            _ => "•",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}

public sealed class CountToVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DriveIsSelectedConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return false;
        }

        var letter = values[0] as string;
        var roots = values[1] as string;
        if (string.IsNullOrEmpty(letter) || string.IsNullOrEmpty(roots))
        {
            return false;
        }

        var rootsList = roots.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var root = $"{letter}\\";
        return rootsList.Any(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase));
    }

    public object? ConvertBack(IList<object?>? values, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RiskToBrushConverter : IValueConverter
{
    private static readonly IBrush Safe = new SolidColorBrush(Color.Parse("#34C08A"));
    private static readonly IBrush Medium = new SolidColorBrush(Color.Parse("#E0B34A"));
    private static readonly IBrush Advanced = new SolidColorBrush(Color.Parse("#E05B5B"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RiskLevel level
            ? level switch
            {
                RiskLevel.Safe => Safe,
                RiskLevel.Medium => Medium,
                _ => Advanced,
            }
            : Brushes.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RiskToLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RiskLevel level ? level.Label() : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ConfidenceToBrushConverter : IValueConverter
{
    private static readonly IBrush High = new SolidColorBrush(Color.Parse("#34C08A"));
    private static readonly IBrush Mid = new SolidColorBrush(Color.Parse("#3AA2FF"));
    private static readonly IBrush Low = new SolidColorBrush(Color.Parse("#E0B34A"));
    private static readonly IBrush Keep = new SolidColorBrush(Color.Parse("#E05B5B"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is byte confidence
            ? confidence switch
            {
                >= 95 => High,
                >= 80 => Mid,
                >= 50 => Low,
                _ => Keep,
            }
            : Brushes.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ActionToCheckEnabledConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CandidateAction action && action != CandidateAction.Keep;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BytesToHumanSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            long l => HumanSize.Format(l),
            ulong ul => HumanSize.Format((long)ul),
            int i => HumanSize.Format(i),
            _ => "—",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
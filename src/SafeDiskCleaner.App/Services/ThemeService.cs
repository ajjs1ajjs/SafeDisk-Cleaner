using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace SafeDiskCleaner.App.Services;

/// <summary>
/// Swaps the Neon Clean base palette (dark/light) and the accent preset at
/// runtime by replacing the merged resource dictionaries in Application.Resources.
/// Views reference the base/accent resources via DynamicResource, so the whole UI
/// updates live.
/// </summary>
public sealed class ThemeService
{
    private const string BaseDarkUri = "pack://application:,,,/SafeDiskCleaner;component/Themes/Base/Dark.xaml";
    private const string BaseLightUri = "pack://application:,,,/SafeDiskCleaner;component/Themes/Base/Light.xaml";
    private const string AccentDirectoryUri = "pack://application:,,,/SafeDiskCleaner;component/Themes/Accents/";

    private readonly PaletteHelper _paletteHelper = new();

    public void Apply(bool dark, string accent)
    {
        ApplyMaterialDesign(dark, accent);
        ApplyResourceDictionaries(dark, accent);
    }

    private void ApplyMaterialDesign(bool dark, string accent)
    {
        try
        {
            var theme = _paletteHelper.GetTheme();
            theme.SetBaseTheme(dark ? BaseTheme.Dark : BaseTheme.Light);
            theme.SetPrimaryColor(AccentColor(accent));
            _paletteHelper.SetTheme(theme);
        }
        catch
        {
            // MaterialDesign theme switching is best-effort; the neon palette still applies.
        }
    }

    private void ApplyResourceDictionaries(bool dark, string accent)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var dicts = app.Resources.MergedDictionaries;
        ReplaceDictionary(dicts, "Themes/Base/", dark ? BaseDarkUri : BaseLightUri);
        ReplaceDictionary(dicts, "Themes/Accents/", $"{AccentDirectoryUri}{accent}.xaml");
    }

    private static void ReplaceDictionary(Collection<ResourceDictionary> dicts, string fragment, string newUri)
    {
        var index = -1;
        for (var i = 0; i < dicts.Count; i++)
        {
            if (dicts[i].Source?.OriginalString.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true)
            {
                index = i;
                break;
            }
        }

        var replacement = new ResourceDictionary { Source = new Uri(newUri) };
        if (index >= 0)
        {
            dicts.RemoveAt(index);
            dicts.Insert(index, replacement);
        }
        else
        {
            dicts.Insert(0, replacement);
        }
    }

    public static Color AccentColor(string accent) => accent switch
    {
        "Purple" => Color.FromRgb(0xA8, 0x55, 0xF7),
        "Green" => Color.FromRgb(0x22, 0xC5, 0x5E),
        "Amber" => Color.FromRgb(0xF5, 0x9E, 0x0B),
        _ => Color.FromRgb(0x00, 0xE5, 0xFF),
    };
}
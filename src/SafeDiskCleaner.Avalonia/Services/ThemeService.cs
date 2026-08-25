using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

namespace SafeDiskCleaner.App.Services;

/// <summary>
/// Swaps the Neon Clean base palette (dark/light) and the accent preset at
/// runtime by replacing the merged resource dictionaries in Application.Resources.
/// Views reference the base/accent resources via DynamicResource, so the whole UI
/// updates live. The FluentTheme variant follows the dark/light choice.
/// </summary>
public sealed class ThemeService : SafeDiskCleaner.ViewModels.Abstractions.IThemeService
{
    private const string BaseDark = "avares://SafeDiskCleaner/Themes/Base.Dark.axaml";
    private const string BaseLight = "avares://SafeDiskCleaner/Themes/Base.Light.axaml";
    private const string AccentPrefix = "avares://SafeDiskCleaner/Themes/Accent.";

    public void Apply(bool dark, string accent)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var dicts = app.Resources.MergedDictionaries;
        ReplaceInclude(dicts, "/Themes/Base.", dark ? BaseDark : BaseLight);
        ReplaceInclude(dicts, "/Themes/Accent.", $"{AccentPrefix}{accent}.axaml");
    }

    private static void ReplaceInclude(
        IList<IResourceProvider> dicts,
        string fragment,
        string newSource)
    {
        var index = -1;
        for (var i = 0; i < dicts.Count; i++)
        {
            if (dicts[i] is ResourceInclude { Source: { } source } &&
                source.ToString().Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        var replacement = new ResourceInclude(new Uri(newSource));
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
        "Purple" => Color.Parse("#A855F7"),
        "Green" => Color.Parse("#22C55E"),
        "Amber" => Color.Parse("#F59E0B"),
        _ => Color.Parse("#00E5FF"),
    };
}
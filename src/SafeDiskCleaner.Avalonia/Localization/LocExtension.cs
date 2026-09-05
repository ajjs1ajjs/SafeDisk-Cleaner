using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SafeDiskCleaner.Core.Localization;

namespace SafeDiskCleaner.Avalonia.Localization;

/// <summary>
/// Markup extension for localized strings: <c>Text="{loc:Loc Common.Cancel}"</c>.
/// Returns a one-way binding to the localization indexer, so bound values update
/// automatically when the language changes at runtime. Non-property targets
/// (e.g. DataGrid column headers) receive a snapshot string instead.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocExtension()
    {
        Key = string.Empty;
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding("[" + Key + "]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        };

        var targetProvider = serviceProvider?.GetService(typeof(IProvideValueTarget)) as global::Avalonia.Markup.Xaml.IProvideValueTarget;
        if (targetProvider?.TargetProperty is global::Avalonia.AvaloniaProperty)
        {
            return binding;
        }

        return LocalizationService.Instance[Key];
    }
}

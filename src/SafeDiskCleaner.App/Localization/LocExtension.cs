using System.Windows.Markup;
using SafeDiskCleaner.Core.Localization;

namespace SafeDiskCleaner.App.Localization;

/// <summary>
/// Markup extension for localized strings: <c>Text="{loc:Loc Common.Cancel}"</c>.
/// Returns a one-way binding to the localization indexer, so bound values update
/// automatically when the language changes at runtime.
/// </summary>
[MarkupExtensionReturnType(typeof(System.Windows.Data.Binding))]
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

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new System.Windows.Data.Binding("[" + Key + "]")
        {
            Source = LocalizationService.Instance,
            Mode = System.Windows.Data.BindingMode.OneWay,
        };
}

using System.Windows.Markup;
using SafeDiskCleaner.Core.Localization;

namespace SafeDiskCleaner.App.Localization;

/// <summary>
/// Markup extension for localized strings: <c>Text="{loc:Loc Common.Cancel}"</c>.
/// Returns a one-way binding to the localization indexer, so bound values update
/// automatically when the language changes at runtime. Non-dependency-property
/// targets (e.g. DataGrid column headers) receive a snapshot string instead.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
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
        var binding = new System.Windows.Data.Binding("[" + Key + "]")
        {
            Source = LocalizationService.Instance,
            Mode = System.Windows.Data.BindingMode.OneWay,
        };

        var targetProvider = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        if (targetProvider?.TargetProperty is System.Windows.DependencyProperty)
        {
            return binding.ProvideValue(serviceProvider);
        }

        // Plain CLR properties (DataGridColumn.Header etc.) cannot hold a
        // binding expression — give them the current translation.
        return LocalizationService.Instance[Key];
    }
}

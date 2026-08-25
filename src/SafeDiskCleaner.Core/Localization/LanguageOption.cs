namespace SafeDiskCleaner.Core.Localization;

/// <summary>A UI language choice: catalog code plus native display name.</summary>
public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

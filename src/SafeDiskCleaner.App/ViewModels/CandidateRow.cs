using CommunityToolkit.Mvvm.ComponentModel;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Utils;

namespace SafeDiskCleaner.App.ViewModels;

/// <summary>Wraps a scan candidate with a UI-selectable flag.</summary>
public sealed class CandidateRow : ObservableObject
{
    private bool _isSelected;

    public CandidateRow(Candidate item)
    {
        Item = item;
    }

    public Candidate Item { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Path => Item.Path;
    public long Size => Item.Size;
    public string SizeText => HumanSize.Format(Item.Size);
    public Category Category => Item.Category;
    public string CategoryLabel => Item.Category.Label();
    public RiskLevel RiskLevel => Item.RiskLevel;
    public byte Confidence => Item.Confidence;
    public string Recommendation => Core.Confidence.ConfidenceEngine.Recommendation(Item.Confidence);
    public string Reason => Item.Reason;
    public string LastAccessText => Item.LastAccessDays is { } days && days != uint.MaxValue ? $"{days} дн." : "—";
    public bool IsSelectable => Item.Action != CandidateAction.Keep;
}

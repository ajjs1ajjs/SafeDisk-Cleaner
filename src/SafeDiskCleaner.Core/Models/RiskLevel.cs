namespace SafeDiskCleaner.Core.Models;

public enum RiskLevel
{
    Safe,
    Medium,
    Advanced,
}

public static class RiskLevelExtensions
{
    public static string Label(this RiskLevel level) => level switch
    {
        RiskLevel.Safe => "Safe",
        RiskLevel.Medium => "Medium",
        RiskLevel.Advanced => "Advanced",
        _ => level.ToString(),
    };
}

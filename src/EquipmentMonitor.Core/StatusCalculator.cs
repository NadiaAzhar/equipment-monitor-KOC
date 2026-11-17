namespace EquipmentMonitor.Core;

public static class EquipmentStatus
{
    public const string Alarm = "ALARM";
    public const string Warning = "WARNING";
    public const string Normal = "NORMAL";

    /// <summary>ALARM if any HIGH_ALARM; else WARNING if any HIGH_WARNING or ROLLING_AVERAGE_HIGH; else NORMAL.</summary>
    public static string FromRuleIds(IEnumerable<string> ruleIds)
    {
        var status = Normal;
        foreach (var rule in ruleIds)
        {
            if (rule == RuleIds.HighAlarm) return Alarm;
            if (rule is RuleIds.HighWarning or RuleIds.RollingAverageHigh) status = Warning;
        }
        return status;
    }

    public static int Rank(string status) => status switch { Alarm => 0, Warning => 1, _ => 2 };
}

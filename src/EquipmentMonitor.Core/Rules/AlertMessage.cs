using System.Globalization;

namespace EquipmentMonitor.Core.Rules;

internal static class AlertMessage
{
    private static string N(double v) => v.ToString("0.0##", CultureInfo.InvariantCulture);
    private static string Sensor(string s) => s.ToLowerInvariant();

    private static string Reported(RuleInput i) =>
        i.Reading.Unit == i.Limit.Unit ? "" : $"; reported {N(i.Reading.Value)} {i.Reading.Unit}";

    public static string Point(string ruleId, string limitName, RuleInput i, double limit) =>
        $"{ruleId}: {Sensor(i.Limit.Sensor)} {N(i.Value)} {i.Limit.Unit} ({limitName} limit {N(limit)} {i.Limit.Unit}{Reported(i)})";

    public static string Rolling(RuleInput i, int windowMinutes) =>
        $"{RuleIds.RollingAverageHigh}: {Sensor(i.Limit.Sensor)} {windowMinutes}-min average {N((double)i.Window.Average)} {i.Limit.Unit} " +
        $"over {i.Window.GoodCount} GOOD readings (rolling average limit {N(i.Limit.RollingAvgHigh)} {i.Limit.Unit})";
}

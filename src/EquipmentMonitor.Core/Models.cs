namespace EquipmentMonitor.Core;

public sealed record Equipment(
    string EquipmentId, string EquipmentType, string Description,
    string SiteId, string SiteName, string Criticality);

public sealed record SensorLimit(
    string EquipmentId, string Sensor, string Measures, string Unit,
    double WarningHigh, double AlarmHigh, double RollingAvgHigh);

public sealed record Reading(
    DateTimeOffset Timestamp, string EquipmentId, string Sensor,
    double Value, string Unit, string Quality, long SourceRow = 0)
{
    public bool IsGood => Quality == Quality_Good;
    public const string Quality_Good = "GOOD";
    public const string Quality_Bad = "BAD";
}

/// <summary>
/// One broken rule. <see cref="Value"/> is what was compared with <see cref="Limit"/>, in the limit's
/// unit (the 15-minute average for ROLLING_AVERAGE_HIGH). <see cref="OriginalValue"/> and
/// <see cref="OriginalUnit"/> are the triggering reading as the sensor reported it.
/// </summary>
public sealed record Alert(
    string RuleId, string Severity, string EquipmentId, string Sensor, DateTimeOffset Timestamp,
    double Value, string Unit, double Limit, double OriginalValue, string OriginalUnit,
    int? WindowGoodCount, string Message, long SourceRow = 0);

public static class RuleIds
{
    public const string HighWarning = "HIGH_WARNING";
    public const string HighAlarm = "HIGH_ALARM";
    public const string RollingAverageHigh = "ROLLING_AVERAGE_HIGH";
    public static readonly IReadOnlyList<string> All = [HighAlarm, HighWarning, RollingAverageHigh];
}

public static class Severities
{
    public const string Alarm = "ALARM";
    public const string Warning = "WARNING";
}

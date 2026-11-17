namespace EquipmentMonitor.Core.Rules;

public sealed class HighWarningRule : IAlertRule
{
    public string RuleId => RuleIds.HighWarning;

    public Alert? Evaluate(RuleInput i)
    {
        var limit = i.Limit.WarningHigh;
        if (!(i.Value >= limit)) return null;
        return new Alert(RuleId, Severities.Warning, i.Reading.EquipmentId, i.Reading.Sensor, i.Reading.Timestamp,
            i.Value, i.Limit.Unit, limit, i.Reading.Value, i.Reading.Unit, null,
            AlertMessage.Point(RuleId, "warning", i, limit), i.Reading.SourceRow);
    }
}

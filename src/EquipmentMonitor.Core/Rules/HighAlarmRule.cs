namespace EquipmentMonitor.Core.Rules;

public sealed class HighAlarmRule : IAlertRule
{
    public string RuleId => RuleIds.HighAlarm;

    public Alert? Evaluate(RuleInput i)
    {
        var limit = i.Limit.AlarmHigh;
        if (!(i.Value >= limit)) return null;
        return new Alert(RuleId, Severities.Alarm, i.Reading.EquipmentId, i.Reading.Sensor, i.Reading.Timestamp,
            i.Value, i.Limit.Unit, limit, i.Reading.Value, i.Reading.Unit, null,
            AlertMessage.Point(RuleId, "alarm", i, limit), i.Reading.SourceRow);
    }
}

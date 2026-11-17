namespace EquipmentMonitor.Core.Rules;

public sealed class RollingAverageHighRule(int minGoodReadings, int windowMinutes) : IAlertRule
{
    public string RuleId => RuleIds.RollingAverageHigh;

    public Alert? Evaluate(RuleInput i)
    {
        if (i.Window.GoodCount < minGoodReadings) return null;
        var limit = i.Limit.RollingAvgHigh;
        if (i.Window.Average < (decimal)limit) return null;
        return new Alert(RuleId, Severities.Warning, i.Reading.EquipmentId, i.Reading.Sensor, i.Reading.Timestamp,
            (double)i.Window.Average, i.Limit.Unit, limit, i.Reading.Value, i.Reading.Unit, i.Window.GoodCount,
            AlertMessage.Rolling(i, windowMinutes), i.Reading.SourceRow);
    }
}

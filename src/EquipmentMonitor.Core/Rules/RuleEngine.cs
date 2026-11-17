namespace EquipmentMonitor.Core.Rules;

/// <summary>
/// Runs every rule over every GOOD reading. Readings are grouped per sensor and sorted by time, so late
/// (out-of-order) readings land in their correct window. BAD readings raise nothing and never enter a window.
/// A repeated (equipment, sensor, timestamp) is evaluated once.
/// </summary>
public sealed class RuleEngine
{
    private readonly RuleOptions _options;
    private readonly UnitConverter _converter;
    private readonly IReadOnlyList<IAlertRule> _rules;

    public RuleEngine(RuleOptions options)
    {
        if (options.RollingWindowMinutes < 1) throw new ArgumentException("RollingWindowMinutes must be 1 or more.");
        if (options.RollingMinGoodReadings < 1) throw new ArgumentException("RollingMinGoodReadings must be 1 or more.");
        _options = options;
        _converter = new UnitConverter(options.UnitConversions);
        _rules =
        [
            new HighAlarmRule(),
            new HighWarningRule(),
            new RollingAverageHighRule(options.RollingMinGoodReadings, options.RollingWindowMinutes),
        ];
    }

    public List<Alert> Evaluate(IEnumerable<Reading> readings, IEnumerable<SensorLimit> limits)
    {
        var limitBySensor = limits.ToDictionary(l => (l.EquipmentId, l.Sensor));
        var alerts = new List<Alert>();
        var window = TimeSpan.FromMinutes(_options.RollingWindowMinutes);

        foreach (var group in readings.GroupBy(r => (r.EquipmentId, r.Sensor)))
        {
            if (!limitBySensor.TryGetValue(group.Key, out var limit)) continue;

            var buffer = new Queue<(DateTimeOffset Ts, decimal Value)>();
            decimal sum = 0;
            DateTimeOffset? previous = null;

            foreach (var reading in group.OrderBy(r => r.Timestamp).ThenBy(r => r.SourceRow))
            {
                if (reading.Timestamp == previous) continue;
                previous = reading.Timestamp;

                if (!reading.IsGood) continue;
                if (!_converter.TryConvert(reading.Value, reading.Unit, limit.Unit, out var value)) continue;
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;

                // Window is (t - window, t]: for 15 minutes that is the readings from t-14 to t on 1-minute data.
                while (buffer.Count > 0 && buffer.Peek().Ts <= reading.Timestamp - window)
                    sum -= buffer.Dequeue().Value;
                var dec = (decimal)value;
                buffer.Enqueue((reading.Timestamp, dec));
                sum += dec;

                var input = new RuleInput(reading, value, limit, new WindowStats(buffer.Count, sum / buffer.Count));
                foreach (var rule in _rules)
                    if (rule.Evaluate(input) is { } alert) alerts.Add(alert);
            }
        }
        return alerts;
    }
}

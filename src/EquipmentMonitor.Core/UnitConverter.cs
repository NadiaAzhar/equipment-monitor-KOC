namespace EquipmentMonitor.Core;

public sealed class UnitConverter(IEnumerable<UnitConversion> conversions)
{
    private readonly Dictionary<(string, string), Func<double, double>> _map = Build(conversions);

    private static Dictionary<(string, string), Func<double, double>> Build(IEnumerable<UnitConversion> conversions)
    {
        var map = new Dictionary<(string, string), Func<double, double>>();
        foreach (var c in conversions)
        {
            if (c.FromPerTo <= 0) throw new ArgumentException($"Conversion {c.From}->{c.To} needs a positive factor.");
            var factor = c.FromPerTo;
            map[(c.From, c.To)] = v => v / factor;
            map[(c.To, c.From)] = v => v * factor;
        }
        return map;
    }

    public bool TryConvert(double value, string fromUnit, string toUnit, out double result)
    {
        if (fromUnit == toUnit) { result = value; return true; }
        if (_map.TryGetValue((fromUnit, toUnit), out var f)) { result = f(value); return true; }
        result = double.NaN;
        return false;
    }

    public bool CanConvert(string fromUnit, string toUnit) => fromUnit == toUnit || _map.ContainsKey((fromUnit, toUnit));
}

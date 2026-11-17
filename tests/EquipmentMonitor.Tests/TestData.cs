using EquipmentMonitor.Core;

namespace EquipmentMonitor.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Start = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

    public static RuleOptions Options(int window = 15, int minGood = 10) => new()
    {
        RollingWindowMinutes = window,
        RollingMinGoodReadings = minGood,
        UnitConversions = [new UnitConversion { From = "psi", To = "bar", FromPerTo = 14.5038 }],
    };

    public static SensorLimit Vibration(double warning = 7.1, double alarm = 11.2, double rolling = 6.0) =>
        new("P-101", "VIBRATION", "Casing vibration", "mm/s", warning, alarm, rolling);

    public static SensorLimit Pressure(double warning = 70, double alarm = 75, double rolling = 68) =>
        new("K-203", "PRESSURE", "Discharge pressure", "bar", warning, alarm, rolling);

    public static Reading At(int minute, double value, string quality = "GOOD", string equipment = "P-101",
        string sensor = "VIBRATION", string unit = "mm/s", long row = 0) =>
        new(Start.AddMinutes(minute), equipment, sensor, value, unit, quality, row == 0 ? minute + 1 : row);

    /// <summary>One GOOD reading a minute from <paramref name="fromMinute"/>, all with <paramref name="value"/>.</summary>
    public static IEnumerable<Reading> Steady(int fromMinute, int count, double value) =>
        Enumerable.Range(fromMinute, count).Select(m => At(m, value));
}

namespace EquipmentMonitor.Core;

public sealed class MonitorOptions
{
    public const string Section = "Monitor";

    /// <summary>Folder with equipment.csv, sensor_limits.csv and sensor_readings.csv. Relative paths resolve from the content root.</summary>
    public string DataDirectory { get; set; } = "../../data";
    public string DatabasePath { get; set; } = "equipment.db";
    /// <summary>Operator local time. Kuwait is UTC+03:00 with no daylight saving time.</summary>
    public TimeSpan LocalUtcOffset { get; set; } = TimeSpan.FromHours(3);
    public RuleOptions Rules { get; set; } = new();
}

public sealed class RuleOptions
{
    public int RollingWindowMinutes { get; set; } = 15;
    public int RollingMinGoodReadings { get; set; } = 10;
    public List<UnitConversion> UnitConversions { get; set; } = [];
}

/// <summary>1 <see cref="To"/> = <see cref="FromPerTo"/> <see cref="From"/>; for example 1 bar = 14.5038 psi.</summary>
public sealed class UnitConversion
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public double FromPerTo { get; set; }
}

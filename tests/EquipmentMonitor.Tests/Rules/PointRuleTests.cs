using EquipmentMonitor.Core;
using EquipmentMonitor.Core.Rules;
using static EquipmentMonitor.Tests.TestData;

namespace EquipmentMonitor.Tests.Rules;

public class PointRuleTests
{
    private static List<Alert> Run(params Reading[] readings) =>
        new RuleEngine(Options()).Evaluate(readings, [Vibration(), Pressure()]);

    [Theory]
    [InlineData(7.1, true)]       // exactly at the warning limit
    [InlineData(7.0999, false)]   // just below
    [InlineData(7.06, false)]     // would round to 7.1, but must not alert
    [InlineData(7.1001, true)]
    public void HighWarning_Boundaries(double value, bool expected)
    {
        var alerts = Run(At(0, value)).Where(a => a.RuleId == RuleIds.HighWarning).ToList();
        Assert.Equal(expected, alerts.Count == 1);
    }

    [Theory]
    [InlineData(11.2, true)]
    [InlineData(11.1999, false)]
    public void HighAlarm_Boundaries(double value, bool expected)
    {
        var alerts = Run(At(0, value)).Where(a => a.RuleId == RuleIds.HighAlarm).ToList();
        Assert.Equal(expected, alerts.Count == 1);
    }

    [Fact]
    public void HighAlarm_ValueAboveAlarm_AlsoRaisesHighWarning()
    {
        var rules = Run(At(0, 11.6)).Select(a => a.RuleId).OrderBy(r => r).ToList();
        Assert.Equal([RuleIds.HighAlarm, RuleIds.HighWarning], rules);
    }

    [Fact]
    public void HighAlarm_Message_ExplainsRuleSensorReadingAndLimit()
    {
        var alert = Run(At(0, 11.6)).Single(a => a.RuleId == RuleIds.HighAlarm);
        Assert.Equal("HIGH_ALARM: vibration 11.6 mm/s (alarm limit 11.2 mm/s)", alert.Message);
        Assert.Equal(11.6, alert.Value);
        Assert.Equal(11.2, alert.Limit);
        Assert.Equal(Severities.Alarm, alert.Severity);
    }

    [Fact]
    public void BadReading_FaultValue_RaisesNothing()
    {
        Assert.Empty(Run(At(0, 999.9, quality: "BAD")));
    }

    [Theory]
    [InlineData(1015.3, true)]   // 1015.3 psi = 70.0024 bar >= 70 bar warning
    [InlineData(1015.2, false)]  // 1015.2 psi = 69.9955 bar
    public void HighWarning_PsiReading_ConvertedToBarBeforeComparing(double psi, bool expected)
    {
        var alerts = Run(At(0, psi, equipment: "K-203", sensor: "PRESSURE", unit: "psi"))
            .Where(a => a.RuleId == RuleIds.HighWarning).ToList();
        Assert.Equal(expected, alerts.Count == 1);
        if (expected)
        {
            var a = alerts[0];
            Assert.Equal("bar", a.Unit);
            Assert.Equal(psi / 14.5038, a.Value, 12);
            Assert.Equal(psi, a.OriginalValue);
            Assert.Equal("psi", a.OriginalUnit);
            Assert.Contains("reported 1015.3 psi", a.Message);
        }
    }

    [Fact]
    public void Psi_ValueAboveBarLimitButNotConverted_WouldFalselyAlarm_IsNotRaised()
    {
        // 950 psi = 65.5 bar: below every K-203 limit, even though 950 > 75.
        Assert.Empty(Run(At(0, 950, equipment: "K-203", sensor: "PRESSURE", unit: "psi")));
    }

    [Fact]
    public void UnknownUnit_ReadingIsSkipped()
    {
        Assert.Empty(Run(At(0, 50, equipment: "K-203", sensor: "PRESSURE", unit: "kPa")));
    }

    [Fact]
    public void Duplicate_SameTimestamp_AlertedOnce()
    {
        var alerts = Run(At(5, 8.0, row: 6), At(5, 8.0, row: 99));
        Assert.Single(alerts, a => a.RuleId == RuleIds.HighWarning);
    }

    [Fact]
    public void ReadingWithoutLimit_IsIgnored()
    {
        Assert.Empty(Run(At(0, 500, equipment: "X-999")));
    }
}

using EquipmentMonitor.Core;
using EquipmentMonitor.Core.Rules;
using static EquipmentMonitor.Tests.TestData;

namespace EquipmentMonitor.Tests.Rules;

public class RollingAverageRuleTests
{
    private static List<Alert> Rolling(IEnumerable<Reading> readings, params SensorLimit[] limits) =>
        new RuleEngine(Options()).Evaluate(readings, limits.Length > 0 ? limits : [Vibration(rolling: 6.0)])
            .Where(a => a.RuleId == RuleIds.RollingAverageHigh).ToList();

    [Fact]
    public void NineGoodReadings_AtStartOfDay_RaisesNothing()
    {
        Assert.Empty(Rolling(Steady(0, 9, 6.5)));
    }

    [Fact]
    public void TenthGoodReading_AverageAboveLimit_Raises()
    {
        var alerts = Rolling(Steady(0, 10, 6.5));
        var a = Assert.Single(alerts);
        Assert.Equal(Start.AddMinutes(9), a.Timestamp);
        Assert.Equal(10, a.WindowGoodCount);
        Assert.Equal(6.5, a.Value, 12);
        Assert.Equal(Severities.Warning, a.Severity);
    }

    [Fact]
    public void AverageExactlyAtLimit_Raises()
    {
        // 5.5 and 6.5 alternate: average of 10 = 6.0 exactly (decimal sum, no float drift).
        var readings = Enumerable.Range(0, 10).Select(m => At(m, m % 2 == 0 ? 5.5 : 6.5));
        var a = Assert.Single(Rolling(readings));
        Assert.Equal(6.0, a.Value);
    }

    [Fact]
    public void AverageAtLimitWithInexactBinaryValues_Raises()
    {
        // 0.1 + 0.2 style drift: ten readings of 0.6 against a 0.6 limit.
        var limit = Vibration(rolling: 0.6);
        Assert.Single(Rolling(Steady(0, 10, 0.6), limit));
    }

    [Fact]
    public void AverageJustBelowLimit_RaisesNothing()
    {
        Assert.Empty(Rolling(Steady(0, 15, 5.9999)));
    }

    [Fact]
    public void BadReadingsInWindow_DoNotCount()
    {
        // 9 GOOD + 1 BAD fault value: too few GOOD, and 999.9 must not inflate the average.
        var readings = Steady(0, 9, 6.5).Append(At(9, 999.9, quality: "BAD"));
        Assert.Empty(Rolling(readings));
    }

    [Fact]
    public void BadReading_DoesNotRaiseAverage()
    {
        var readings = Steady(0, 10, 5.0).Append(At(10, 999.9, quality: "BAD")).Append(At(11, 5.0));
        Assert.Empty(Rolling(readings));
    }

    [Fact]
    public void Window_IsFifteenMinutesInclusiveOfCurrent()
    {
        // Minute 0 is high, 1..15 low. At minute 14 the window is 0..14 (includes the spike); at 15 it's 1..15.
        var readings = new[] { At(0, 100.0) }.Concat(Steady(1, 15, 1.0)).ToList();
        var alerts = Rolling(readings);
        Assert.Contains(alerts, a => a.Timestamp == Start.AddMinutes(14) && a.WindowGoodCount == 15);
        Assert.DoesNotContain(alerts, a => a.Timestamp == Start.AddMinutes(15));
    }

    [Fact]
    public void AfterGap_WindowRestartsAndNeedsTenGoodReadings()
    {
        var before = Steady(0, 20, 6.5);
        var after = Steady(40, 12, 6.5); // 20-minute gap: window empties
        var alerts = Rolling(before.Concat(after));
        Assert.DoesNotContain(alerts, a => a.Timestamp >= Start.AddMinutes(40) && a.Timestamp < Start.AddMinutes(49));
        Assert.Contains(alerts, a => a.Timestamp == Start.AddMinutes(49) && a.WindowGoodCount == 10);
    }

    [Fact]
    public void ShortGapInsideWindow_StillAlertsWhenTenGoodRemain()
    {
        // Minutes 0-11 then 14: window at 14 holds 0..11 + 14 = 13 GOOD readings.
        var readings = Steady(0, 12, 6.5).Append(At(14, 6.5));
        Assert.Contains(Rolling(readings), a => a.Timestamp == Start.AddMinutes(14) && a.WindowGoodCount == 13);
    }

    [Fact]
    public void LateReadings_GiveSameAlertsAsInOrder()
    {
        var inOrder = Steady(0, 30, 6.5).ToList();
        var late = inOrder.Skip(10).Concat(inOrder.Take(10)).ToList(); // backfill appended at the end
        Assert.Equal(Rolling(inOrder).Select(a => a.Timestamp), Rolling(late).Select(a => a.Timestamp));
    }

    [Fact]
    public void OtherSensors_DoNotMixIntoWindow()
    {
        var readings = Steady(0, 5, 6.5)
            .Concat(Enumerable.Range(0, 10).Select(m => At(m, 9.0, equipment: "P-102")))
            .Concat(Enumerable.Range(5, 4).Select(m => At(m, 6.5)));
        var limits = new[] { Vibration(), Vibration() with { EquipmentId = "P-102", RollingAvgHigh = 100 } };
        Assert.Empty(Rolling(readings, limits));
    }

    [Fact]
    public void PsiReadings_AveragedInBar()
    {
        // 1000 psi = 68.948 bar >= 68 bar rolling limit.
        var readings = Enumerable.Range(0, 10).Select(m => At(m, 1000, equipment: "K-203", sensor: "PRESSURE", unit: "psi"));
        var a = Assert.Single(Rolling(readings, Pressure()));
        Assert.Equal(1000 / 14.5038, a.Value, 9);
        Assert.Equal("bar", a.Unit);
    }

    [Fact]
    public void Message_ExplainsAverageCountAndLimit()
    {
        var a = Assert.Single(Rolling(Steady(0, 10, 6.5)));
        Assert.Equal("ROLLING_AVERAGE_HIGH: vibration 15-min average 6.5 mm/s over 10 GOOD readings (rolling average limit 6.0 mm/s)", a.Message);
    }

    [Fact]
    public void WindowSizeAndMinimum_ComeFromOptions()
    {
        var alerts = new RuleEngine(Options(window: 5, minGood: 3)).Evaluate(Steady(0, 3, 6.5), [Vibration()])
            .Where(a => a.RuleId == RuleIds.RollingAverageHigh);
        Assert.Single(alerts);
    }
}

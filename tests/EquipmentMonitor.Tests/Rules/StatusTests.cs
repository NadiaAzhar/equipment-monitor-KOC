using EquipmentMonitor.Core;

namespace EquipmentMonitor.Tests.Rules;

public class StatusTests
{
    [Theory]
    [InlineData("NORMAL")]
    [InlineData("WARNING", "HIGH_WARNING")]
    [InlineData("WARNING", "ROLLING_AVERAGE_HIGH")]
    [InlineData("ALARM", "HIGH_ALARM")]
    [InlineData("ALARM", "ROLLING_AVERAGE_HIGH", "HIGH_WARNING", "HIGH_ALARM")]
    [InlineData("ALARM", "HIGH_ALARM", "ROLLING_AVERAGE_HIGH")]
    public void Status_MostSevereAlertWins(string expected, params string[] rules)
    {
        Assert.Equal(expected, EquipmentStatus.FromRuleIds(rules));
    }
}

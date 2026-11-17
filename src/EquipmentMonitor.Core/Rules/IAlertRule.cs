namespace EquipmentMonitor.Core.Rules;

/// <summary>Rolling-window statistics over GOOD readings, in the limit's unit, up to and including the current reading.</summary>
public readonly record struct WindowStats(int GoodCount, decimal Average);

/// <summary>A GOOD reading already converted to its limit's unit.</summary>
public sealed record RuleInput(Reading Reading, double Value, SensorLimit Limit, WindowStats Window);

public interface IAlertRule
{
    string RuleId { get; }
    Alert? Evaluate(RuleInput input);
}

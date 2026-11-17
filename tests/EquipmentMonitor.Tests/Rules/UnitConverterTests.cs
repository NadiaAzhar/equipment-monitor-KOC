using EquipmentMonitor.Core;

namespace EquipmentMonitor.Tests.Rules;

public class UnitConverterTests
{
    private readonly UnitConverter _converter = new(TestData.Options().UnitConversions);

    [Fact]
    public void Psi_ToBar_DividesByFactor()
    {
        Assert.True(_converter.TryConvert(14.5038, "psi", "bar", out var bar));
        Assert.Equal(1.0, bar, 12);
    }

    [Fact]
    public void Bar_ToPsi_IsInverse()
    {
        Assert.True(_converter.TryConvert(1, "bar", "psi", out var psi));
        Assert.Equal(14.5038, psi, 12);
    }

    [Fact]
    public void SameUnit_ReturnsValueUnchanged()
    {
        Assert.True(_converter.TryConvert(7.06, "mm/s", "mm/s", out var v));
        Assert.Equal(7.06, v);
    }

    [Fact]
    public void UnknownPair_Fails()
    {
        Assert.False(_converter.TryConvert(1, "kPa", "bar", out _));
    }
}

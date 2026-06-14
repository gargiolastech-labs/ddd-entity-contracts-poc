using Domain.SharedKernel;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.SharedKernel;

public class DeltaTests
{
    [Fact]
    public void Delta_Unchanged_PreservesValue()
    {
        var delta = Delta<string>.Unchanged("hello");

        delta.IsChanged.Should().BeFalse();
        delta.Value.Should().Be("hello");
    }

    [Fact]
    public void Delta_Unchanged_PreservesNullValue()
    {
        var delta = Delta<string>.Unchanged(null);

        delta.IsChanged.Should().BeFalse();
        delta.Value.Should().BeNull();
    }

    [Fact]
    public void Delta_Changed_TracksNewValue()
    {
        var delta = Delta<string>.Changed("world");

        delta.IsChanged.Should().BeTrue();
        delta.Value.Should().Be("world");
    }

    [Fact]
    public void Delta_Changed_TracksNewValueIncludingNull()
    {
        var delta = Delta<string>.Changed(null);

        delta.IsChanged.Should().BeTrue();
        delta.Value.Should().BeNull();
    }

    [Fact]
    public void Delta_Equality_DependsOnValueAndFlag()
    {
        var a = Delta<string>.Changed("x");
        var b = Delta<string>.Changed("x");
        var c = Delta<string>.Unchanged("x");

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void Delta_From_ReturnsUnchanged_WhenValuesAreEqual()
    {
        var delta = Delta<string>.From("same", "same");

        delta.IsChanged.Should().BeFalse();
        delta.Value.Should().Be("same");
    }

    [Fact]
    public void Delta_From_ReturnsChanged_WhenValuesAreDifferent()
    {
        var delta = Delta<string>.From("old", "new");

        delta.IsChanged.Should().BeTrue();
        delta.Value.Should().Be("new");
    }

    [Fact]
    public void Delta_From_TreatsNullAsLegitimateChangedValue()
    {
        var delta = Delta<string>.From("old", null);

        delta.IsChanged.Should().BeTrue();
        delta.Value.Should().BeNull();
    }
}

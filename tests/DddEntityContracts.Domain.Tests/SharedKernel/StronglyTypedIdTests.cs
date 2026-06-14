using Domain.SharedKernel;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.SharedKernel;

public class StronglyTypedIdTests
{
    [Fact]
    public void StronglyTypedId_ValueEquality_SameValueAreEqual()
    {
        var guid = Guid.NewGuid();
        var id1 = StronglyTypedId.From(guid);
        var id2 = StronglyTypedId.From(guid);

        id1.Should().Be(id2);
    }

    [Fact]
    public void StronglyTypedId_ValueEquality_DifferentValueAreNotEqual()
    {
        var id1 = StronglyTypedId.From(Guid.NewGuid());
        var id2 = StronglyTypedId.From(Guid.NewGuid());

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void StronglyTypedId_New_GeneratesDistinctValues()
    {
        var id1 = StronglyTypedId.New();
        var id2 = StronglyTypedId.New();

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void StronglyTypedId_From_PreservesValue()
    {
        var guid = Guid.NewGuid();
        var id = StronglyTypedId.From(guid);

        id.Value.Should().Be(guid);
    }

    [Fact]
    public void StronglyTypedId_ToString_ReturnsGuidString()
    {
        var guid = Guid.NewGuid();
        var id = StronglyTypedId.From(guid);

        id.ToString().Should().Be(guid.ToString());
    }
}

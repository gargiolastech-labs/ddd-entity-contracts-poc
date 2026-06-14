using Domain.SharedKernel;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.SharedKernel;

public class ValueObjectGuardsTests
{
    [Fact]
    public void ValueObjectGuards_NotNullOrWhiteSpace_ReturnsFailure_WhenNull()
    {
        var result = ValueObjectGuards.NotNullOrWhiteSpace(null, "ERR001", "Value is required.");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ValueObjectGuards_NotNullOrWhiteSpace_ReturnsFailure_WhenWhiteSpace()
    {
        var result = ValueObjectGuards.NotNullOrWhiteSpace("   ", "ERR001", "Value is required.");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ValueObjectGuards_NotNullOrWhiteSpace_ReturnsSuccess_WhenValid()
    {
        var result = ValueObjectGuards.NotNullOrWhiteSpace("hello", "ERR001", "Value is required.");

        result.IsSuccess.Should().BeTrue();
        result.ToResult().Value.Should().Be("hello");
    }

    [Fact]
    public void ValueObjectGuards_MaxLength_ReturnsSuccess_WhenLengthIsEqualToLimit()
    {
        var value = new string('a', 10);

        var result = ValueObjectGuards.MaxLength(value, 10, "ERR002", "Too long.");

        result.IsSuccess.Should().BeTrue();
        result.ToResult().Value.Should().Be(value);
    }

    [Fact]
    public void ValueObjectGuards_MaxLength_ReturnsFailure_WhenLengthExceedsLimit()
    {
        var value = new string('a', 11);

        var result = ValueObjectGuards.MaxLength(value, 10, "ERR002", "Too long.");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ValueObjectGuards_Matches_ReturnsSuccess_WhenPatternMatches()
    {
        var result = ValueObjectGuards.Matches(
            "test@example.com",
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            "ERR003",
            "Invalid email.");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValueObjectGuards_Matches_ReturnsFailure_WhenPatternDoesNotMatch()
    {
        var result = ValueObjectGuards.Matches(
            "not-an-email",
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            "ERR003",
            "Invalid email.");

        result.IsFailure.Should().BeTrue();
    }
}

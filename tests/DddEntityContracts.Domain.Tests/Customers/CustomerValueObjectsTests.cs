using Domain.Customers;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.Customers;

public class CustomerValueObjectsTests
{
    [Fact]
    public void CustomerName_Create_ReturnsSuccess_ForValidInput()
    {
        var result = CustomerName.Create("Alice");

        result.IsSuccess.Should().BeTrue();
        result.ToResult().Value.Value.Should().Be("Alice");
    }

    [Fact]
    public void CustomerName_Create_ReturnsFailure_ForNullOrEmpty()
    {
        var result = CustomerName.Create(null);

        result.IsFailure.Should().BeTrue();
        result.ToResult().Errors.Should().Contain(e => e.Code == "CustomerName.Required");
    }

    [Fact]
    public void Email_Create_NormalizesInput()
    {
        var result = Email.Create("  ALICE@EXAMPLE.COM  ");

        result.IsSuccess.Should().BeTrue();
        result.ToResult().Value.Value.Should().Be("alice@example.com");
    }

    [Fact]
    public void Email_Create_ReturnsFailure_ForInvalidFormat()
    {
        var result = Email.Create("not-an-email");

        result.IsFailure.Should().BeTrue();
        result.ToResult().Errors.Should().Contain(e => e.Code == "Email.InvalidFormat");
    }

    [Fact]
    public void PhoneNumber_Create_ReturnsSuccess_WhenAbsent()
    {
        var result = PhoneNumber.Create(null);

        result.IsSuccess.Should().BeTrue();
        result.ToResult().Value.Value.Should().BeNull();
    }
}

using Domain.Customers;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.Customers;

public class CustomerCreationTests
{
    private static CreateCustomerRequest ValidRequest() =>
        new("Alice Rossi", "alice@example.com", null);

    [Fact]
    public void Create_WithValidRequest_Succeeds()
    {
        var result = Customer.Create(ValidRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Value.Should().Be("Alice Rossi");
        result.Value.Email.Value.Should().Be("alice@example.com");
        result.Value.Phone.Value.Should().BeNull();
    }

    [Fact]
    public void Create_WithInvalidEmail_ReturnsFailure()
    {
        var result = Customer.Create(new CreateCustomerRequest("Alice Rossi", "not-an-email", null));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Email.InvalidFormat");
    }

    [Fact]
    public void Create_WithMultipleInvalidFields_AccumulatesErrors()
    {
        var result = Customer.Create(new CreateCustomerRequest(null, null, null));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "CustomerName.Required");
        result.Errors.Should().Contain(e => e.Code == "Email.Required");
    }

    [Fact]
    public void Create_WithValidRequest_RaisesCustomerCreatedEvent()
    {
        var result = Customer.Create(ValidRequest());

        result.Value.DomainEvents.Should().ContainSingle(e => e is CustomerCreated);
    }
}

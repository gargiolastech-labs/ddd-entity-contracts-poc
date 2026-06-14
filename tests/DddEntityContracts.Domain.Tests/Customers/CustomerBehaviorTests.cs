using Domain.Customers;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.Customers;

public class CustomerBehaviorTests
{
    private static Customer CreateValidCustomer() =>
        Customer.Create(new CreateCustomerRequest("Alice Rossi", "alice@example.com", null)).Value;

    [Fact]
    public void Create_InitializesStatusAsActive()
    {
        var customer = CreateValidCustomer();

        customer.Status.Should().Be(CustomerStatus.Active);
    }

    [Fact]
    public void Deactivate_FromActive_SetsInactive_RaisesCustomerDeactivated()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.Deactivate("test reason");

        result.IsSuccess.Should().BeTrue();
        customer.Status.Should().Be(CustomerStatus.Inactive);
        customer.DomainEvents.Should().ContainSingle(e => e is CustomerDeactivated);
        var evt = (CustomerDeactivated)customer.DomainEvents.Single();
        evt.Reason.Should().Be("test reason");
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_IsIdempotent_NoMutation_NoEvent()
    {
        var customer = CreateValidCustomer();
        customer.Deactivate();
        customer.ClearDomainEvents();

        var result = customer.Deactivate();

        result.IsSuccess.Should().BeTrue();
        customer.Status.Should().Be(CustomerStatus.Inactive);
        customer.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Activate_FromInactive_RaisesCustomerActivated()
    {
        var customer = CreateValidCustomer();
        customer.Deactivate();
        customer.ClearDomainEvents();

        var result = customer.Activate();

        result.IsSuccess.Should().BeTrue();
        customer.Status.Should().Be(CustomerStatus.Active);
        customer.DomainEvents.Should().ContainSingle(e => e is CustomerActivated);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_IsIdempotent_NoMutation_NoEvent()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.Activate();

        result.IsSuccess.Should().BeTrue();
        customer.Status.Should().Be(CustomerStatus.Active);
        customer.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Status_IsNotPartOf_UpdateCustomerRequest()
    {
        var properties = typeof(UpdateCustomerRequest).GetProperties();

        properties.Should().NotContain(p => p.Name == "Status");
    }
}

using Domain.Customers;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.Customers;

public class CustomerUpdateTests
{
    private static Customer CreateValidCustomer() =>
        Customer.Create(new CreateCustomerRequest("Alice Rossi", "alice@example.com", null)).Value;

    [Fact]
    public void Update_WithNoChanges_IsIdempotent_NoEventsNoMutation()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.Update(new UpdateCustomerRequest("Alice Rossi", "alice@example.com", null));

        result.IsSuccess.Should().BeTrue();
        customer.Name.Value.Should().Be("Alice Rossi");
        customer.Email.Value.Should().Be("alice@example.com");
        customer.Phone.Value.Should().BeNull();
        customer.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_SingleFieldChanged_EmitsCustomerUpdated_WithThatFieldOnly()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.Update(new UpdateCustomerRequest("Alice Rossi", "newalice@example.com", null));

        result.IsSuccess.Should().BeTrue();
        customer.Email.Value.Should().Be("newalice@example.com");
        customer.Name.Value.Should().Be("Alice Rossi");
        customer.Phone.Value.Should().BeNull();
        customer.DomainEvents.Should().ContainSingle(e => e is CustomerUpdated);
        var evt = (CustomerUpdated)customer.DomainEvents.Single();
        evt.ChangedFields.Should().BeEquivalentTo(new[] { "Email" });
    }

    [Fact]
    public void Update_MultipleFieldsChanged_EmitsOneCustomerUpdated_WithAllChangedFields()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.Update(new UpdateCustomerRequest("Bob Smith", "bob@example.com", "+39 012 345678"));

        result.IsSuccess.Should().BeTrue();
        customer.Name.Value.Should().Be("Bob Smith");
        customer.Email.Value.Should().Be("bob@example.com");
        customer.Phone.Value.Should().Be("+39 012 345678");
        customer.DomainEvents.Should().ContainSingle(e => e is CustomerUpdated);
        var evt = (CustomerUpdated)customer.DomainEvents.Single();
        evt.ChangedFields.Should().BeEquivalentTo(new[] { "Name", "Email", "Phone" });
    }

    [Fact]
    public void Update_InvalidField_NoPartialMutation_NoEvents()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.Update(new UpdateCustomerRequest("Bob Smith", "not-an-email", "+39 012 345678"));

        result.IsFailure.Should().BeTrue();
        customer.Name.Value.Should().Be("Alice Rossi");
        customer.Email.Value.Should().Be("alice@example.com");
        customer.Phone.Value.Should().BeNull();
        customer.DomainEvents.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Code == "Email.InvalidFormat");
    }

    [Fact]
    public void Update_ClearPhoneToNull_MarksPhoneAsChanged_InCustomerUpdated()
    {
        var customer = Customer.Create(
            new CreateCustomerRequest("Alice Rossi", "alice@example.com", "+39 012 345678")).Value;
        customer.ClearDomainEvents();

        var result = customer.Update(new UpdateCustomerRequest("Alice Rossi", "alice@example.com", null));

        result.IsSuccess.Should().BeTrue();
        customer.Phone.Value.Should().BeNull();
        customer.DomainEvents.Should().ContainSingle(e => e is CustomerUpdated);
        var evt = (CustomerUpdated)customer.DomainEvents.Single();
        evt.ChangedFields.Should().BeEquivalentTo(new[] { "Phone" });
    }

    [Fact]
    public void Update_CrossFieldInvariantViolated_FailsOnFullState()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.Update(
            new UpdateCustomerRequest("Alice Rossi", "alice@internal.local", null));

        result.IsFailure.Should().BeTrue();
        customer.Email.Value.Should().Be("alice@example.com");
        customer.DomainEvents.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Code == "Customer.InternalEmailRequiresPhone");
    }
}

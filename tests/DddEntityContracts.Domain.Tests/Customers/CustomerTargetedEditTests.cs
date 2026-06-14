using Domain.Customers;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.Customers;

public class CustomerTargetedEditTests
{
    private static Customer CreateValidCustomer() =>
        Customer.Create(new CreateCustomerRequest("Alice Rossi", "alice@example.com", null)).Value;

    [Fact]
    public void ChangeEmail_Valid_RaisesCustomerUpdated_EmailOnly()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.ChangeEmail("newalice@example.com");

        result.IsSuccess.Should().BeTrue();
        customer.Email.Value.Should().Be("newalice@example.com");
        customer.Name.Value.Should().Be("Alice Rossi");
        customer.Phone.Value.Should().BeNull();
        customer.DomainEvents.Should().ContainSingle(e => e is CustomerUpdated);
        var evt = (CustomerUpdated)customer.DomainEvents.Single();
        evt.ChangedFields.Should().BeEquivalentTo(new[] { "Email" });
    }

    [Fact]
    public void ChangeEmail_Invalid_NoMutation_NoEvent()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.ChangeEmail("not-an-email");

        result.IsFailure.Should().BeTrue();
        customer.Email.Value.Should().Be("alice@example.com");
        customer.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangeEmail_SameValue_IsIdempotent()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.ChangeEmail("ALICE@EXAMPLE.COM");

        result.IsSuccess.Should().BeTrue();
        customer.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangePhone_ToNull_MarksPhoneChanged()
    {
        var customer = Customer.Create(
            new CreateCustomerRequest("Alice Rossi", "alice@example.com", "+39 012 345678")).Value;
        customer.ClearDomainEvents();

        var result = customer.ChangePhone(null);

        result.IsSuccess.Should().BeTrue();
        customer.Phone.Value.Should().BeNull();
        customer.DomainEvents.Should().ContainSingle(e => e is CustomerUpdated);
        var evt = (CustomerUpdated)customer.DomainEvents.Single();
        evt.ChangedFields.Should().BeEquivalentTo(new[] { "Phone" });
    }

    [Fact]
    public void TargetedEdit_CrossFieldInvariant_ReEvaluatedOnFullState()
    {
        var customer = CreateValidCustomer();
        customer.ClearDomainEvents();

        var result = customer.ChangeEmail("person@internal.local");

        result.IsFailure.Should().BeTrue();
        customer.Email.Value.Should().Be("alice@example.com");
        customer.DomainEvents.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Code == "Customer.InternalEmailRequiresPhone");
    }
}

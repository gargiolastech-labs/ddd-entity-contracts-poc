using Application.Customers.EventHandlers;
using DddEntityContracts.Application.Tests.Fakes;
using Domain.Customers;
using FluentAssertions;

namespace DddEntityContracts.Application.Tests.Customers.EventHandlers;

public class CustomerCreatedWelcomeEmailHandlerTests
{
    [Fact]
    public async Task CustomerCreated_TriggersWelcomeEmail()
    {
        var customerId = CustomerId.New();
        var domainEvent = new CustomerCreated(customerId);

        var notificationSender = new FakeCustomerNotificationSender();
        var sut = new CustomerCreatedWelcomeEmailHandler(notificationSender);

        await sut.HandleAsync(domainEvent);

        notificationSender.Calls.Should().ContainSingle();
        notificationSender.Calls[0].Method.Should().Be("SendWelcomeEmailAsync");
        notificationSender.Calls[0].CustomerId.Should().Be(customerId.Value);
    }

    [Fact]
    public async Task CustomerCreated_WelcomeEmailHandler_DoesNotWriteToOutbox()
    {
        var domainEvent = new CustomerCreated(CustomerId.New());
        var notificationSender = new FakeCustomerNotificationSender();
        var sut = new CustomerCreatedWelcomeEmailHandler(notificationSender);

        await sut.HandleAsync(domainEvent);

        // This handler is responsible only for side-effect notifications,
        // not for producing outbox messages.
        notificationSender.Calls.Should().HaveCount(1);
    }
}

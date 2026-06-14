using System.Text.Json;
using Application.Contracts.IntegrationEvents;
using Application.Customers.EventHandlers;
using DddEntityContracts.Application.Tests.Fakes;
using Domain.Customers;
using FluentAssertions;

namespace DddEntityContracts.Application.Tests.Customers.EventHandlers;

public class CustomerDeactivatedToIntegrationEventHandlerTests
{
    [Fact]
    public async Task CustomerDeactivated_MappedTo_IntegrationEvent()
    {
        var customerId = CustomerId.New();
        const string reason = "Account suspended by admin";
        var domainEvent = new CustomerDeactivated(customerId, reason);

        var outbox = new FakeOutboxWriter();
        var sut = new CustomerDeactivatedToIntegrationEventHandler(outbox);

        await sut.HandleAsync(domainEvent);

        outbox.Messages.Should().ContainSingle();

        var message = outbox.Messages[0];
        message.Type.Should().Be("CustomerDeactivated.v1");
        message.DeduplicationKey.Should().Be($"CustomerDeactivated:{customerId.Value}");

        var integrationEvent = JsonSerializer.Deserialize<CustomerDeactivatedIntegrationEvent>(message.Payload);
        integrationEvent.Should().NotBeNull();
        integrationEvent!.CustomerId.Should().Be(customerId.Value);
        integrationEvent.Reason.Should().Be(reason);
        integrationEvent.Version.Should().Be(1);
        integrationEvent.EventId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CustomerDeactivated_WithNullReason_PreservesNullInIntegrationEvent()
    {
        var customerId = CustomerId.New();
        var domainEvent = new CustomerDeactivated(customerId, Reason: null);

        var outbox = new FakeOutboxWriter();
        var sut = new CustomerDeactivatedToIntegrationEventHandler(outbox);

        await sut.HandleAsync(domainEvent);

        var message = outbox.Messages[0];
        var integrationEvent = JsonSerializer.Deserialize<CustomerDeactivatedIntegrationEvent>(message.Payload);
        integrationEvent!.Reason.Should().BeNull();
    }

    [Fact]
    public async Task CustomerDeactivated_IntegrationEvent_PayloadContainsOnlyPrimitives()
    {
        // The integration event contract is declared with Guid and string primitives —
        // no domain Value Object types cross the boundary. This is a structural guarantee
        // enforced at compile time; we verify the serialized payload is well-formed.
        var rawId = Guid.NewGuid();
        var domainEvent = new CustomerDeactivated(CustomerId.From(rawId), "test");

        var outbox = new FakeOutboxWriter();
        var sut = new CustomerDeactivatedToIntegrationEventHandler(outbox);

        await sut.HandleAsync(domainEvent);

        var integrationEvent = JsonSerializer.Deserialize<CustomerDeactivatedIntegrationEvent>(
            outbox.Messages[0].Payload);

        // CustomerId round-trips as a plain Guid value.
        integrationEvent!.CustomerId.Should().Be(rawId);
        integrationEvent.Reason.Should().Be("test");
    }
}

using System.Text.Json;
using Application.Contracts.IntegrationEvents;
using Application.Customers.EventHandlers;
using DddEntityContracts.Application.Tests.Fakes;
using Domain.Customers;
using FluentAssertions;

namespace DddEntityContracts.Application.Tests.Customers.EventHandlers;

public class CustomerCreatedToIntegrationEventHandlerTests
{
    [Fact]
    public async Task CustomerCreated_MappedTo_CustomerRegisteredIntegrationEvent_EnqueuedToOutbox()
    {
        var customerId = CustomerId.New();
        var domainEvent = new CustomerCreated(customerId);

        var outbox = new FakeOutboxWriter();
        var sut = new CustomerCreatedToIntegrationEventHandler(outbox);

        await sut.HandleAsync(domainEvent);

        outbox.Messages.Should().ContainSingle();

        var message = outbox.Messages[0];
        message.Type.Should().Be("CustomerRegistered.v1");
        message.DeduplicationKey.Should().Be($"CustomerRegistered:{customerId.Value}");
        message.OccurredAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        var integrationEvent = JsonSerializer.Deserialize<CustomerRegisteredIntegrationEvent>(message.Payload);
        integrationEvent.Should().NotBeNull();
        integrationEvent!.CustomerId.Should().Be(customerId.Value);
        integrationEvent.Version.Should().Be(1);
        integrationEvent.EventId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IntegrationEventHandler_IsIdempotent_OnRedelivery()
    {
        var customerId = CustomerId.New();
        var domainEvent = new CustomerCreated(customerId);

        var outbox = new FakeOutboxWriter();
        var sut = new CustomerCreatedToIntegrationEventHandler(outbox);

        // Simulate at-least-once redelivery of the same domain event.
        await sut.HandleAsync(domainEvent);
        await sut.HandleAsync(domainEvent);

        outbox.Messages.Should().HaveCount(2);

        var keys = outbox.Messages.Select(m => m.DeduplicationKey).Distinct();
        keys.Should().ContainSingle("same domain event redelivered must produce the same deduplication key");
        // Deduplication enforcement (discarding the second enqueue) is the
        // responsibility of the outbox storage layer, not this handler.
    }
}

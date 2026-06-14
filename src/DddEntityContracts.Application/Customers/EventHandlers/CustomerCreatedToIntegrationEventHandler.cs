using System.Text.Json;
using Application.Abstractions;
using Application.Abstractions.Outbox;
using Application.Contracts.IntegrationEvents;
using Domain.Customers;

namespace Application.Customers.EventHandlers;

public sealed class CustomerCreatedToIntegrationEventHandler : IDomainEventHandler<CustomerCreated>
{
    private readonly IOutboxWriter _outboxWriter;

    public CustomerCreatedToIntegrationEventHandler(IOutboxWriter outboxWriter)
    {
        _outboxWriter = outboxWriter;
    }

    public Task HandleAsync(CustomerCreated domainEvent, CancellationToken cancellationToken = default)
    {
        var customerId = domainEvent.CustomerId.Value;

        var integrationEvent = new CustomerRegisteredIntegrationEvent(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Version: 1,
            CustomerId: customerId);

        var message = new OutboxMessage(
            Id: Guid.NewGuid(),
            Type: "CustomerRegistered.v1",
            Payload: JsonSerializer.Serialize(integrationEvent),
            OccurredAtUtc: integrationEvent.OccurredAtUtc,
            DeduplicationKey: $"CustomerRegistered:{customerId}");

        return _outboxWriter.EnqueueAsync(message, cancellationToken);
    }
}

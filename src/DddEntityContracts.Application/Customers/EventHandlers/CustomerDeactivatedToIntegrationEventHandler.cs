using System.Text.Json;
using Application.Abstractions;
using Application.Abstractions.Outbox;
using Application.Contracts.IntegrationEvents;
using Domain.Customers;

namespace Application.Customers.EventHandlers;

public sealed class CustomerDeactivatedToIntegrationEventHandler : IDomainEventHandler<CustomerDeactivated>
{
    private readonly IOutboxWriter _outboxWriter;

    public CustomerDeactivatedToIntegrationEventHandler(IOutboxWriter outboxWriter)
    {
        _outboxWriter = outboxWriter;
    }

    public Task HandleAsync(CustomerDeactivated domainEvent, CancellationToken cancellationToken = default)
    {
        var customerId = domainEvent.CustomerId.Value;

        var integrationEvent = new CustomerDeactivatedIntegrationEvent(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Version: 1,
            CustomerId: customerId,
            Reason: domainEvent.Reason);

        // Trade-off: key is stable per customer, covering redelivery of the same event.
        // For multiple legitimate deactivations (after reactivations), the outbox store
        // should scope deduplication by event id or timestamp in production.
        var message = new OutboxMessage(
            Id: Guid.NewGuid(),
            Type: "CustomerDeactivated.v1",
            Payload: JsonSerializer.Serialize(integrationEvent),
            OccurredAtUtc: integrationEvent.OccurredAtUtc,
            DeduplicationKey: $"CustomerDeactivated:{customerId}");

        return _outboxWriter.EnqueueAsync(message, cancellationToken);
    }
}

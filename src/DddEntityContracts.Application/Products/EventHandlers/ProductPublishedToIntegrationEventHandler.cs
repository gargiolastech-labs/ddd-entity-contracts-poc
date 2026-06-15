using System.Text.Json;
using Application.Abstractions;
using Application.Abstractions.Outbox;
using Application.Contracts.IntegrationEvents;
using Domain.Products;

namespace Application.Products.EventHandlers;

public sealed class ProductPublishedToIntegrationEventHandler : IDomainEventHandler<ProductPublished>
{
    private readonly IOutboxWriter _outboxWriter;

    public ProductPublishedToIntegrationEventHandler(IOutboxWriter outboxWriter)
    {
        _outboxWriter = outboxWriter;
    }

    public Task HandleAsync(ProductPublished domainEvent, CancellationToken cancellationToken = default)
    {
        var productId = domainEvent.ProductId.Value;

        var integrationEvent = new ProductPublishedIntegrationEvent(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Version: 1,
            ProductId: productId);

        var message = new OutboxMessage(
            Id: Guid.NewGuid(),
            Type: "ProductPublished.v1",
            Payload: JsonSerializer.Serialize(integrationEvent),
            OccurredAtUtc: integrationEvent.OccurredAtUtc,
            DeduplicationKey: $"ProductPublished:{productId}");

        return _outboxWriter.EnqueueAsync(message, cancellationToken);
    }
}

namespace Application.Contracts.IntegrationEvents;

public sealed record ProductPublishedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    int Version,
    Guid ProductId)
    : IntegrationEvent(EventId, OccurredAtUtc, Version);

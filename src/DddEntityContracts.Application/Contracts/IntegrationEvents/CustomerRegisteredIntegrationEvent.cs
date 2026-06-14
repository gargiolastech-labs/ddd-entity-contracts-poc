namespace Application.Contracts.IntegrationEvents;

public sealed record CustomerRegisteredIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    int Version,
    Guid CustomerId)
    : IntegrationEvent(EventId, OccurredAtUtc, Version);

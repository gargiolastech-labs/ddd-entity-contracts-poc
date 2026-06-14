namespace Application.Contracts.IntegrationEvents;

public sealed record CustomerDeactivatedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    int Version,
    Guid CustomerId,
    string? Reason)
    : IntegrationEvent(EventId, OccurredAtUtc, Version);

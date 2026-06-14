namespace Application.Contracts.IntegrationEvents;

public abstract record IntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    int Version);

namespace Application.Abstractions.Outbox;

public sealed record OutboxMessage(
    Guid Id,
    string Type,
    string Payload,
    DateTimeOffset OccurredAtUtc,
    string? DeduplicationKey = null);

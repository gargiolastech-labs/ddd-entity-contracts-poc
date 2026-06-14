namespace Application.Abstractions.Outbox;

public interface IOutboxWriter
{
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

using Application.Abstractions.Outbox;

namespace DddEntityContracts.Application.Tests.Fakes;

internal sealed class FakeOutboxWriter : IOutboxWriter
{
    public List<OutboxMessage> Messages { get; } = [];

    public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

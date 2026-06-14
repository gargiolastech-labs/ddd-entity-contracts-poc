using Application.Abstractions.Notifications;
using Application.Abstractions.Outbox;

namespace DddEntityContracts.Api.Stubs;

// No-op implementations registered in the API host for PoC / dev runs.
// Production replaces these with real infrastructure (DB-backed outbox, SMTP, etc.).

internal sealed class InMemoryOutboxWriter : IOutboxWriter
{
    private readonly List<OutboxMessage> _messages = [];

    public IReadOnlyList<OutboxMessage> Messages => _messages;

    public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class NoOpCustomerNotificationSender : ICustomerNotificationSender
{
    public Task SendWelcomeEmailAsync(Guid customerId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SendCustomerDeactivatedNotificationAsync(
        Guid customerId,
        string? reason,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

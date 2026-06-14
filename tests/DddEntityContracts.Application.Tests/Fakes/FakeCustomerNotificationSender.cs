using Application.Abstractions.Notifications;

namespace DddEntityContracts.Application.Tests.Fakes;

internal sealed class FakeCustomerNotificationSender : ICustomerNotificationSender
{
    public List<(Guid CustomerId, string Method)> Calls { get; } = [];

    public Task SendWelcomeEmailAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        Calls.Add((customerId, nameof(SendWelcomeEmailAsync)));
        return Task.CompletedTask;
    }

    public Task SendCustomerDeactivatedNotificationAsync(
        Guid customerId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((customerId, nameof(SendCustomerDeactivatedNotificationAsync)));
        return Task.CompletedTask;
    }
}

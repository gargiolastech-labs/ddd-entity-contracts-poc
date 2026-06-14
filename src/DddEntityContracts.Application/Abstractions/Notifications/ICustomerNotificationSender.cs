namespace Application.Abstractions.Notifications;

public interface ICustomerNotificationSender
{
    Task SendWelcomeEmailAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task SendCustomerDeactivatedNotificationAsync(
        Guid customerId,
        string? reason,
        CancellationToken cancellationToken = default);
}

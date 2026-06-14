using Application.Abstractions;
using Application.Abstractions.Notifications;
using Domain.Customers;

namespace Application.Customers.EventHandlers;

public sealed class CustomerCreatedWelcomeEmailHandler : IDomainEventHandler<CustomerCreated>
{
    private readonly ICustomerNotificationSender _notificationSender;

    public CustomerCreatedWelcomeEmailHandler(ICustomerNotificationSender notificationSender)
    {
        _notificationSender = notificationSender;
    }

    public Task HandleAsync(CustomerCreated domainEvent, CancellationToken cancellationToken = default)
        => _notificationSender.SendWelcomeEmailAsync(domainEvent.CustomerId.Value, cancellationToken);
}

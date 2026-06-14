using Application.Abstractions;
using Application.Customers.EventHandlers;
using Domain.Customers;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventHandler<CustomerCreated>, CustomerCreatedWelcomeEmailHandler>();
        services.AddScoped<IDomainEventHandler<CustomerCreated>, CustomerCreatedToIntegrationEventHandler>();
        services.AddScoped<IDomainEventHandler<CustomerDeactivated>, CustomerDeactivatedToIntegrationEventHandler>();
        return services;
    }
}

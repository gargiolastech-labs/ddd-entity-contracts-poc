using Application.Abstractions;
using Application.Customers.EventHandlers;
using Application.Products.EventHandlers;
using Domain.Customers;
using Domain.Products;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventHandler<CustomerCreated>, CustomerCreatedWelcomeEmailHandler>();
        services.AddScoped<IDomainEventHandler<CustomerCreated>, CustomerCreatedToIntegrationEventHandler>();
        services.AddScoped<IDomainEventHandler<CustomerDeactivated>, CustomerDeactivatedToIntegrationEventHandler>();
        services.AddScoped<IDomainEventHandler<ProductPublished>, ProductPublishedToIntegrationEventHandler>();
        return services;
    }
}

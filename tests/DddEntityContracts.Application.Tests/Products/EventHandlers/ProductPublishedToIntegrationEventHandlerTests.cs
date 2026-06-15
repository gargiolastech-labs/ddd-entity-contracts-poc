using System.Text.Json;
using Application.Contracts.IntegrationEvents;
using Application.Products.EventHandlers;
using DddEntityContracts.Application.Tests.Fakes;
using Domain.Products;
using FluentAssertions;

namespace DddEntityContracts.Application.Tests.Products.EventHandlers;

public class ProductPublishedToIntegrationEventHandlerTests
{
    [Fact]
    public async Task ProductPublished_MappedTo_ProductPublishedIntegrationEvent_EnqueuedToOutbox()
    {
        var productId = ProductId.New();
        var domainEvent = new ProductPublished(productId);

        var outbox = new FakeOutboxWriter();
        var sut = new ProductPublishedToIntegrationEventHandler(outbox);

        await sut.HandleAsync(domainEvent);

        outbox.Messages.Should().ContainSingle();

        var message = outbox.Messages[0];
        message.Type.Should().Be("ProductPublished.v1");
        message.DeduplicationKey.Should().Be($"ProductPublished:{productId.Value}");
        message.OccurredAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        var integrationEvent = JsonSerializer.Deserialize<ProductPublishedIntegrationEvent>(message.Payload);
        integrationEvent.Should().NotBeNull();
        integrationEvent!.ProductId.Should().Be(productId.Value);
        integrationEvent.Version.Should().Be(1);
        integrationEvent.EventId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ProductPublished_PayloadContainsOnlyPrimitives()
    {
        // Guard structurale: ProductId nel DTO è Guid, non ProductId VO.
        var rawId = Guid.NewGuid();
        var domainEvent = new ProductPublished(ProductId.From(rawId));

        var outbox = new FakeOutboxWriter();
        var sut = new ProductPublishedToIntegrationEventHandler(outbox);

        await sut.HandleAsync(domainEvent);

        var integrationEvent = JsonSerializer.Deserialize<ProductPublishedIntegrationEvent>(
            outbox.Messages[0].Payload);
        integrationEvent!.ProductId.Should().Be(rawId);
    }

    [Fact]
    public async Task ProductPublishedHandler_IsIdempotent_OnRedelivery()
    {
        var productId = ProductId.New();
        var domainEvent = new ProductPublished(productId);

        var outbox = new FakeOutboxWriter();
        var sut = new ProductPublishedToIntegrationEventHandler(outbox);

        await sut.HandleAsync(domainEvent);
        await sut.HandleAsync(domainEvent);

        outbox.Messages.Should().HaveCount(2);
        var keys = outbox.Messages.Select(m => m.DeduplicationKey).Distinct();
        keys.Should().ContainSingle("same domain event redelivered must produce the same deduplication key");
    }
}

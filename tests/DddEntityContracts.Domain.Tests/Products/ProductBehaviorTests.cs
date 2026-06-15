using Domain.Products;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.Products;

public class ProductBehaviorTests
{
    private static Product DraftProductWithDescription() => Product.Create(
        new CreateProductRequest("SKU-100", "Widget", "Description.", 10m, "EUR")).Value;

    private static Product DraftProductWithoutDescription() => Product.Create(
        new CreateProductRequest("SKU-100", "Widget", Description: null, 10m, "EUR")).Value;

    // ── Publish ───────────────────────────────────────────────────────────

    [Fact]
    public void Publish_FromDraftWithDescription_RaisesProductPublished()
    {
        var product = DraftProductWithDescription();
        product.ClearDomainEvents();

        var result = product.Publish();

        result.IsSuccess.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Published);
        product.DomainEvents.Should().ContainSingle(e => e is ProductPublished);
    }

    [Fact]
    public void Publish_WithoutDescription_Fails()
    {
        var product = DraftProductWithoutDescription();
        product.ClearDomainEvents();

        var result = product.Publish();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Product.PublishRequiresDescription");
        product.Status.Should().Be(ProductStatus.Draft);
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Publish_AlreadyPublished_IsIdempotent_NoEvent()
    {
        var product = DraftProductWithDescription();
        product.Publish();
        product.ClearDomainEvents();

        var result = product.Publish();

        result.IsSuccess.Should().BeTrue();
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Publish_FromArchived_Fails()
    {
        var product = DraftProductWithDescription();
        product.Publish();
        product.Archive();
        product.ClearDomainEvents();

        var result = product.Publish();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Product.PublishNotAllowed");
        product.Status.Should().Be(ProductStatus.Archived);
    }

    // ── Archive ───────────────────────────────────────────────────────────

    [Fact]
    public void Archive_FromPublished_RaisesProductArchived()
    {
        var product = DraftProductWithDescription();
        product.Publish();
        product.ClearDomainEvents();

        var result = product.Archive("End of season.");

        result.IsSuccess.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Archived);
        var evt = product.DomainEvents.OfType<ProductArchived>().Single();
        evt.Reason.Should().Be("End of season.");
    }

    [Fact]
    public void Archive_WithoutReason_RaisesEventWithNullReason()
    {
        var product = DraftProductWithDescription();
        product.Publish();
        product.ClearDomainEvents();

        var result = product.Archive();

        result.IsSuccess.Should().BeTrue();
        var evt = product.DomainEvents.OfType<ProductArchived>().Single();
        evt.Reason.Should().BeNull();
    }

    [Fact]
    public void Archive_FromDraft_Fails()
    {
        var product = DraftProductWithDescription();
        product.ClearDomainEvents();

        var result = product.Archive();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Product.ArchiveNotAllowed");
        product.Status.Should().Be(ProductStatus.Draft);
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Archive_AlreadyArchived_IsIdempotent_NoEvent()
    {
        var product = DraftProductWithDescription();
        product.Publish();
        product.Archive();
        product.ClearDomainEvents();

        var result = product.Archive();

        result.IsSuccess.Should().BeTrue();
        product.DomainEvents.Should().BeEmpty();
    }
}

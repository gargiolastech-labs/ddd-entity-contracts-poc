using Domain.Products;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.Products;

public class ProductTargetedEditTests
{
    private static Product DraftProduct() => Product.Create(new CreateProductRequest(
        "SKU-100", "Widget", "Description.", 10m, "EUR")).Value;

    [Fact]
    public void ChangePrice_DifferentAmount_RaisesUpdatedWithPriceField()
    {
        var product = DraftProduct();
        product.ClearDomainEvents();

        var result = product.ChangePrice(25m, "EUR");

        result.IsSuccess.Should().BeTrue();
        product.Price.Amount.Should().Be(25m);

        var evt = product.DomainEvents.OfType<ProductUpdated>().Single();
        evt.ChangedFields.Should().BeEquivalentTo(new[] { "Price" });
    }

    [Fact]
    public void ChangePrice_DifferentCurrency_DetectedAsChange()
    {
        var product = DraftProduct();
        product.ClearDomainEvents();

        var result = product.ChangePrice(10m, "USD");

        result.IsSuccess.Should().BeTrue();
        product.Price.Currency.Should().Be("USD");
        product.DomainEvents.OfType<ProductUpdated>().Should().ContainSingle();
    }

    [Fact]
    public void ChangePrice_SameValue_IsIdempotent_NoEvent()
    {
        var product = DraftProduct();
        product.ClearDomainEvents();

        var result = product.ChangePrice(10m, "EUR");

        result.IsSuccess.Should().BeTrue();
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangePrice_InvalidValue_NoMutation_NoEvent()
    {
        var product = DraftProduct();
        var originalAmount = product.Price.Amount;
        product.ClearDomainEvents();

        var result = product.ChangePrice(-5m, "EUR");

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Money.AmountNotPositive");
        product.Price.Amount.Should().Be(originalAmount);
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangePrice_OnPublishedProduct_Fails()
    {
        var product = DraftProduct();
        product.Publish();
        product.ClearDomainEvents();

        var result = product.ChangePrice(25m, "EUR");

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Product.PriceChangeNotAllowed");
        product.Price.Amount.Should().Be(10m);
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangePrice_ToPremium_WithoutDescription_FailsOnCrossFieldInvariant()
    {
        var product = Product.Create(new CreateProductRequest(
            "SKU-100", "Widget", Description: null, 10m, "EUR")).Value;
        product.ClearDomainEvents();

        var result = product.ChangePrice(1500m, "EUR");

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Product.PremiumRequiresDescription");
        product.Price.Amount.Should().Be(10m);
        product.DomainEvents.Should().BeEmpty();
    }
}

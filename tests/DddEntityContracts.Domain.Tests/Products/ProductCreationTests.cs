using Domain.Products;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.Products;

public class ProductCreationTests
{
    private static CreateProductRequest ValidRequest() =>
        new("SKU-001", "Widget", "A small widget.", 9.99m, "EUR");

    [Fact]
    public void Create_WithValidRequest_Succeeds()
    {
        var result = Product.Create(ValidRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.Sku.Value.Should().Be("SKU-001");
        result.Value.Name.Value.Should().Be("Widget");
        result.Value.Description.Value.Should().Be("A small widget.");
        result.Value.Price.Amount.Should().Be(9.99m);
        result.Value.Price.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Create_StartsInDraftStatus()
    {
        var product = Product.Create(ValidRequest()).Value;
        product.Status.Should().Be(ProductStatus.Draft);
    }

    [Fact]
    public void Create_WithoutDescription_Succeeds()
    {
        var result = Product.Create(new CreateProductRequest(
            "SKU-002", "Gadget", Description: null, 5m, "USD"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Description.Value.Should().BeNull();
    }

    [Fact]
    public void Create_WithInvalidSku_Fails()
    {
        var result = Product.Create(new CreateProductRequest(
            "bad sku", "Widget", null, 9.99m, "EUR"));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Sku.InvalidFormat");
    }

    [Fact]
    public void Create_WithMultipleInvalidFields_AccumulatesErrors()
    {
        var result = Product.Create(new CreateProductRequest(
            Sku: null, Name: null, Description: null, PriceAmount: -1m, PriceCurrency: null));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Sku.Required");
        result.Errors.Should().Contain(e => e.Code == "ProductName.Required");
        result.Errors.Should().Contain(e => e.Code == "Money.AmountNotPositive");
        result.Errors.Should().Contain(e => e.Code == "Money.CurrencyRequired");
    }

    [Fact]
    public void Create_PremiumPriceWithoutDescription_FailsWithCrossFieldInvariant()
    {
        var result = Product.Create(new CreateProductRequest(
            "PREMIUM-1", "Premium Widget", Description: null, 1500m, "EUR"));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Product.PremiumRequiresDescription");
    }

    [Fact]
    public void Create_PremiumPriceWithDescription_Succeeds()
    {
        var result = Product.Create(new CreateProductRequest(
            "PREMIUM-2", "Premium Widget", "Top tier.", 1500m, "EUR"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_WithValidRequest_RaisesProductCreatedEvent()
    {
        var product = Product.Create(ValidRequest()).Value;

        product.DomainEvents.Should().ContainSingle(e => e is ProductCreated);
        ((ProductCreated)product.DomainEvents.Single()).ProductId.Should().Be(product.Id);
    }

    [Fact]
    public void Create_NormalizesSkuToUppercase()
    {
        var product = Product.Create(new CreateProductRequest(
            "sku-low", "Widget", null, 9.99m, "EUR")).Value;

        product.Sku.Value.Should().Be("SKU-LOW");
    }
}

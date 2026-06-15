using Domain.Products;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.Products;

public class ProductUpdateTests
{
    private static Product DraftProduct() => Product.Create(new CreateProductRequest(
        "SKU-100", "Widget", "Original.", 10m, "EUR")).Value;

    private static UpdateProductRequest UpdateAllFieldsRequest() =>
        new("SKU-200", "Gadget", "Updated.", 20m, "USD");

    [Fact]
    public void Update_AllFieldsChanged_RaisesUpdatedWithAllChangedFields()
    {
        var product = DraftProduct();
        product.ClearDomainEvents();

        var result = product.Update(UpdateAllFieldsRequest());

        result.IsSuccess.Should().BeTrue();
        product.Sku.Value.Should().Be("SKU-200");
        product.Name.Value.Should().Be("Gadget");
        product.Price.Amount.Should().Be(20m);

        var evt = product.DomainEvents.OfType<ProductUpdated>().Single();
        evt.ChangedFields.Should().BeEquivalentTo(new[] { "Sku", "Name", "Description", "Price" });
    }

    [Fact]
    public void Update_NoChanges_IsIdempotent_NoEvent()
    {
        var product = DraftProduct();
        product.ClearDomainEvents();

        var result = product.Update(new UpdateProductRequest(
            "SKU-100", "Widget", "Original.", 10m, "EUR"));

        result.IsSuccess.Should().BeTrue();
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_OnlyNameChanged_RaisesUpdatedWithOnlyNameField()
    {
        var product = DraftProduct();
        product.ClearDomainEvents();

        var result = product.Update(new UpdateProductRequest(
            "SKU-100", "Widget Pro", "Original.", 10m, "EUR"));

        result.IsSuccess.Should().BeTrue();
        var evt = product.DomainEvents.OfType<ProductUpdated>().Single();
        evt.ChangedFields.Should().BeEquivalentTo(new[] { "Name" });
    }

    [Fact]
    public void Update_InvalidField_NoPartialMutation_NoEvents()
    {
        var product = DraftProduct();
        var originalSku = product.Sku.Value;
        var originalName = product.Name.Value;
        product.ClearDomainEvents();

        var result = product.Update(new UpdateProductRequest(
            "SKU-100", "Gadget", "Updated.", -5m, "EUR"));

        result.IsFailure.Should().BeTrue();
        product.Sku.Value.Should().Be(originalSku);
        product.Name.Value.Should().Be(originalName);
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_ClearingDescriptionToNull_DetectedAsChange()
    {
        var product = DraftProduct();
        product.ClearDomainEvents();

        var result = product.Update(new UpdateProductRequest(
            "SKU-100", "Widget", Description: null, 10m, "EUR"));

        result.IsSuccess.Should().BeTrue();
        product.Description.Value.Should().BeNull();
        var evt = product.DomainEvents.OfType<ProductUpdated>().Single();
        evt.ChangedFields.Should().BeEquivalentTo(new[] { "Description" });
    }

    [Fact]
    public void Update_OnPublishedProduct_Fails()
    {
        var product = DraftProduct();
        product.Publish();
        product.ClearDomainEvents();

        var result = product.Update(UpdateAllFieldsRequest());

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Product.UpdateNotAllowed");
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_CrossFieldInvariantViolated_NoMutation()
    {
        var product = DraftProduct();
        product.ClearDomainEvents();

        var result = product.Update(new UpdateProductRequest(
            "SKU-100", "Widget", Description: null, 1500m, "EUR"));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Product.PremiumRequiresDescription");
        product.Price.Amount.Should().Be(10m);   // unchanged
        product.DomainEvents.Should().BeEmpty();
    }
}

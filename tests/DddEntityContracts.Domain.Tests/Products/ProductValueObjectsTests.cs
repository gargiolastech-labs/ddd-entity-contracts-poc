using Domain.Products;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.Products;

public class ProductValueObjectsTests
{
    // ── ProductId ─────────────────────────────────────────────────────────

    [Fact]
    public void ProductId_From_EmptyGuid_Throws()
    {
        var act = () => ProductId.From(Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact]
    public void ProductId_New_GeneratesNonEmpty()
    {
        var id = ProductId.New();
        id.Value.Should().NotBe(Guid.Empty);
    }

    // ── Sku ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AB-123")]
    [InlineData("PROD-001")]
    [InlineData("X")]
    public void Sku_Create_WithValidValue_Succeeds(string raw)
    {
        var result = Sku.Create(raw).ToResult();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Sku_Create_NormalizesToUppercase()
    {
        var sku = Sku.Create("ab-123").ToResult().Value;
        sku.Value.Should().Be("AB-123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sku_Create_WithEmpty_FailsWithRequired(string? raw)
    {
        var result = Sku.Create(raw).ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Sku.Required");
    }

    [Fact]
    public void Sku_Create_WithSpaces_FailsWithInvalidFormat()
    {
        var result = Sku.Create("AB 123").ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Sku.InvalidFormat");
    }

    [Fact]
    public void Sku_Create_TooLong_FailsWithMaxLength()
    {
        var result = Sku.Create(new string('A', 21)).ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Sku.MaxLength");
    }

    [Fact]
    public void Sku_ValueEquality_SameValueAreEqual()
    {
        var s1 = Sku.Create("AB-123").ToResult().Value;
        var s2 = Sku.Create("ab-123").ToResult().Value;
        s1.Should().Be(s2);
    }

    // ── ProductName ───────────────────────────────────────────────────────

    [Fact]
    public void ProductName_Create_TrimsWhitespace()
    {
        var name = ProductName.Create("  Hello  ").ToResult().Value;
        name.Value.Should().Be("Hello");
    }

    [Fact]
    public void ProductName_Create_TooLong_Fails()
    {
        var result = ProductName.Create(new string('a', 201)).ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "ProductName.MaxLength");
    }

    // ── ProductDescription ────────────────────────────────────────────────

    [Fact]
    public void ProductDescription_Create_NullOrEmpty_IsAbsent()
    {
        var d1 = ProductDescription.Create(null).ToResult().Value;
        var d2 = ProductDescription.Create("").ToResult().Value;
        var d3 = ProductDescription.Create("   ").ToResult().Value;

        d1.Value.Should().BeNull();
        d2.Value.Should().BeNull();
        d3.Value.Should().BeNull();
    }

    [Fact]
    public void ProductDescription_Create_TooLong_Fails()
    {
        var result = ProductDescription.Create(new string('x', 2001)).ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "ProductDescription.MaxLength");
    }

    // ── Money ─────────────────────────────────────────────────────────────

    [Fact]
    public void Money_Create_WithValidAmountAndCurrency_Succeeds()
    {
        var money = Money.Create(99.99m, "eur").ToResult().Value;
        money.Amount.Should().Be(99.99m);
        money.Currency.Should().Be("EUR");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Money_Create_NonPositiveAmount_Fails(decimal amount)
    {
        var result = Money.Create(amount, "EUR").ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Money.AmountNotPositive");
    }

    [Fact]
    public void Money_Create_NullAmount_Fails()
    {
        var result = Money.Create(null, "EUR").ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Money.AmountRequired");
    }

    [Fact]
    public void Money_Create_TooLargeAmount_Fails()
    {
        var result = Money.Create(1_000_000m, "EUR").ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Money.AmountTooLarge");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Money_Create_MissingCurrency_Fails(string? currency)
    {
        var result = Money.Create(10m, currency).ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Money.CurrencyRequired");
    }

    [Theory]
    [InlineData("EURO")]
    [InlineData("EU")]
    [InlineData("E1R")]
    public void Money_Create_InvalidCurrencyFormat_Fails(string currency)
    {
        var result = Money.Create(10m, currency).ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Money.CurrencyInvalidFormat");
    }

    [Fact]
    public void Money_Create_AccumulatesAmountAndCurrencyErrors()
    {
        var result = Money.Create(-1m, "EURO").ToResult();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Money.AmountNotPositive");
        result.Errors.Should().Contain(e => e.Code == "Money.CurrencyInvalidFormat");
    }

    [Fact]
    public void Money_ValueEquality_SameValuesAreEqual()
    {
        var m1 = Money.Create(10m, "eur").ToResult().Value;
        var m2 = Money.Create(10m, "EUR").ToResult().Value;
        m1.Should().Be(m2);
    }
}

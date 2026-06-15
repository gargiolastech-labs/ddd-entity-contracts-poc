using Domain.SharedKernel;

namespace Domain.Products;

public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.NewGuid());

    public static ProductId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("ProductId cannot be empty.", nameof(value));
        return new(value);
    }

    public override string ToString() => Value.ToString();
}

public sealed record Sku
{
    public string Value { get; }

    private Sku(string value) { Value = value; }

    private const string SkuPattern = @"^[A-Z0-9-]+$";

    public static Validation<Sku> Create(string? raw)
    {
        var notEmpty = ValueObjectGuards.NotNullOrWhiteSpace(
            raw, "Sku.Required", "SKU is required.");
        if (notEmpty.IsFailure)
            return Validation<Sku>.Failure(notEmpty.ToResult().Errors);

        var normalized = notEmpty.ToResult().Value.Trim().ToUpperInvariant();

        var vMaxLength = ValueObjectGuards.MaxLength(
            normalized, 20, "Sku.MaxLength", "SKU cannot exceed 20 characters.");
        var vFormat = ValueObjectGuards.Matches(
            normalized, SkuPattern, "Sku.InvalidFormat",
            "SKU must contain only uppercase letters, digits, and dashes.");

        return Validation<Sku>.Combine(vMaxLength, vFormat, (_, _) => new Sku(normalized));
    }
}

public sealed record ProductName
{
    public string Value { get; }

    private ProductName(string value) { Value = value; }

    public static Validation<ProductName> Create(string? raw)
    {
        var notEmpty = ValueObjectGuards.NotNullOrWhiteSpace(
            raw, "ProductName.Required", "Product name is required.");
        if (notEmpty.IsFailure)
            return Validation<ProductName>.Failure(notEmpty.ToResult().Errors);

        var value = notEmpty.ToResult().Value.Trim();

        var maxLength = ValueObjectGuards.MaxLength(
            value, 200, "ProductName.MaxLength", "Product name cannot exceed 200 characters.");
        if (maxLength.IsFailure)
            return Validation<ProductName>.Failure(maxLength.ToResult().Errors);

        return Validation<ProductName>.Success(new ProductName(value));
    }
}

public sealed record ProductDescription
{
    public string? Value { get; }

    private ProductDescription(string? value) { Value = value; }

    public static Validation<ProductDescription> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Validation<ProductDescription>.Success(new ProductDescription((string?)null));

        var trimmed = raw.Trim();

        var maxLength = ValueObjectGuards.MaxLength(
            trimmed, 2000, "ProductDescription.MaxLength",
            "Product description cannot exceed 2000 characters.");
        if (maxLength.IsFailure)
            return Validation<ProductDescription>.Failure(maxLength.ToResult().Errors);

        return Validation<ProductDescription>.Success(new ProductDescription(trimmed));
    }
}

public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    private const decimal MaxAmount = 999_999.99m;
    private const string CurrencyPattern = @"^[A-Z]{3}$";

    public static Validation<Money> Create(decimal? amount, string? currency)
    {
        var vAmount = ValidateAmount(amount);
        var vCurrency = ValidateCurrency(currency);

        return Validation<Money>.Combine(
            vAmount, vCurrency,
            (a, c) => new Money(a, c));
    }

    private static Validation<decimal> ValidateAmount(decimal? amount)
    {
        if (amount is null)
            return Validation<decimal>.Failure(
                new Error("Money.AmountRequired", "Amount is required."));
        if (amount <= 0)
            return Validation<decimal>.Failure(
                new Error("Money.AmountNotPositive", "Amount must be greater than zero."));
        if (amount > MaxAmount)
            return Validation<decimal>.Failure(
                new Error("Money.AmountTooLarge", $"Amount cannot exceed {MaxAmount}."));
        return Validation<decimal>.Success(amount.Value);
    }

    private static Validation<string> ValidateCurrency(string? currency)
    {
        var notEmpty = ValueObjectGuards.NotNullOrWhiteSpace(
            currency, "Money.CurrencyRequired", "Currency is required.");
        if (notEmpty.IsFailure)
            return notEmpty;

        var normalized = notEmpty.ToResult().Value.Trim().ToUpperInvariant();

        return ValueObjectGuards.Matches(
            normalized, CurrencyPattern, "Money.CurrencyInvalidFormat",
            "Currency must be a 3-letter ISO code.");
    }
}

public enum ProductStatus
{
    Draft = 1,
    Published = 2,
    Archived = 3
}

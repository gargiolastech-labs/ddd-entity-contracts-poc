using Domain.SharedKernel;

namespace Domain.Customers;

public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.NewGuid());
    public static CustomerId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public sealed record CustomerName
{
    public string Value { get; }

    private CustomerName(string value) { Value = value; }

    public static Validation<CustomerName> Create(string? raw)
    {
        var notEmpty = ValueObjectGuards.NotNullOrWhiteSpace(
            raw, "CustomerName.Required", "Customer name is required.");
        if (notEmpty.IsFailure)
            return Validation<CustomerName>.Failure(notEmpty.ToResult().Errors);

        var value = notEmpty.ToResult().Value.Trim();

        var maxLength = ValueObjectGuards.MaxLength(
            value, 100, "CustomerName.MaxLength", "Customer name cannot exceed 100 characters.");
        if (maxLength.IsFailure)
            return Validation<CustomerName>.Failure(maxLength.ToResult().Errors);

        return Validation<CustomerName>.Success(new CustomerName(value));
    }
}

public sealed record Email
{
    public string Value { get; }

    private Email(string value) { Value = value; }

    private const string EmailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    public static Validation<Email> Create(string? raw)
    {
        var notEmpty = ValueObjectGuards.NotNullOrWhiteSpace(
            raw, "Email.Required", "Email address is required.");
        if (notEmpty.IsFailure)
            return Validation<Email>.Failure(notEmpty.ToResult().Errors);

        var normalized = notEmpty.ToResult().Value.Trim().ToLowerInvariant();

        var vMaxLength = ValueObjectGuards.MaxLength(
            normalized, 254, "Email.MaxLength", "Email address cannot exceed 254 characters.");
        var vFormat = ValueObjectGuards.Matches(
            normalized, EmailPattern, "Email.InvalidFormat", "Email address format is invalid.");

        return Validation<Email>.Combine(vMaxLength, vFormat, (_, _) => new Email(normalized));
    }
}

public sealed record PhoneNumber
{
    public string? Value { get; }

    private PhoneNumber(string? value) { Value = value; }

    private const string PhonePattern = @"^[\d\s\+\-\(\)]+$";

    public static Validation<PhoneNumber> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Validation<PhoneNumber>.Success(new PhoneNumber((string?)null));

        var trimmed = raw.Trim();

        var vMaxLength = ValueObjectGuards.MaxLength(
            trimmed, 30, "PhoneNumber.MaxLength", "Phone number cannot exceed 30 characters.");
        var vFormat = ValueObjectGuards.Matches(
            trimmed, PhonePattern, "PhoneNumber.InvalidFormat", "Phone number format is invalid.");

        return Validation<PhoneNumber>.Combine(vMaxLength, vFormat, (_, _) => new PhoneNumber(trimmed));
    }
}

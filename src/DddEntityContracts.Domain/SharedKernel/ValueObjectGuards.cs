using System.Text.RegularExpressions;

namespace Domain.SharedKernel;

public static class ValueObjectGuards
{
    public static Validation<string> NotNullOrWhiteSpace(
        string? value,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Validation<string>.Failure(new Error(code, message));

        return Validation<string>.Success(value);
    }

    public static Validation<string> MaxLength(
        string value,
        int maxLength,
        string code,
        string message)
    {
        if (value.Length > maxLength)
            return Validation<string>.Failure(new Error(code, message));

        return Validation<string>.Success(value);
    }

    // An invalid regex pattern is a programming error and will throw ArgumentException by design.
    public static Validation<string> Matches(
        string value,
        string pattern,
        string code,
        string message)
    {
        if (!Regex.IsMatch(value, pattern))
            return Validation<string>.Failure(new Error(code, message));

        return Validation<string>.Success(value);
    }
}

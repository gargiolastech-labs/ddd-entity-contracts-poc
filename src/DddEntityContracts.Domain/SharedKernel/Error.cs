namespace Domain.SharedKernel;

public sealed record Error(string Code, string Message)
{
    public static Error Create(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new Error(code, message);
    }
}

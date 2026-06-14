namespace Domain.SharedKernel;

public sealed class Validation<T>
{
    private readonly T _value;
    private readonly List<Error> _errors;

    private Validation(T value, List<Error> errors)
    {
        _value = value;
        _errors = errors;
        IsSuccess = errors.Count == 0;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public static Validation<T> Success(T value) => new(value, []);
    public static Validation<T> Failure(Error error) => new(default!, [error]);
    public static Validation<T> Failure(IEnumerable<Error> errors)
    {
        var list = errors.ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("A failed validation must contain at least one error.");
        return new(default!, list);
    }

    public Result<T> ToResult() =>
        IsSuccess ? Result<T>.Success(_value) : Result<T>.Failure(_errors);

    public static Validation<TResult> Combine<T1, T2, TResult>(
        Validation<T1> first,
        Validation<T2> second,
        Func<T1, T2, TResult> projector)
    {
        var errors = new List<Error>(first._errors.Count + second._errors.Count);
        errors.AddRange(first._errors);
        errors.AddRange(second._errors);

        return errors.Count > 0
            ? Validation<TResult>.Failure(errors)
            : Validation<TResult>.Success(projector(first._value, second._value));
    }

    public static Validation<TResult> Combine<T1, T2, T3, TResult>(
        Validation<T1> first,
        Validation<T2> second,
        Validation<T3> third,
        Func<T1, T2, T3, TResult> projector)
    {
        var errors = new List<Error>(
            first._errors.Count + second._errors.Count + third._errors.Count);
        errors.AddRange(first._errors);
        errors.AddRange(second._errors);
        errors.AddRange(third._errors);

        return errors.Count > 0
            ? Validation<TResult>.Failure(errors)
            : Validation<TResult>.Success(projector(first._value, second._value, third._value));
    }
}

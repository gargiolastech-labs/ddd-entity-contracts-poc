namespace Domain.SharedKernel;

public class Result
{
    private readonly List<Error> _errors;

    protected Result(bool isSuccess, IEnumerable<Error>? errors = null)
    {
        var errorList = errors?.ToList() ?? [];

        if (isSuccess && errorList.Count > 0)
            throw new InvalidOperationException("A successful result cannot contain errors.");
        if (!isSuccess && errorList.Count == 0)
            throw new InvalidOperationException("A failed result must contain at least one error.");

        IsSuccess = isSuccess;
        _errors = errorList;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public IReadOnlyCollection<Error> Errors => _errors.AsReadOnly();
    public Error? FirstError => _errors.Count > 0 ? _errors[0] : null;

    public static Result Success() => new(true);
    public static Result Failure(Error error) => new(false, [error]);
    public static Result Failure(IEnumerable<Error> errors) => new(false, errors);
}

public class Result<T> : Result
{
    private readonly T _value;

    private Result(bool isSuccess, T value, IEnumerable<Error>? errors = null)
        : base(isSuccess, errors)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Cannot access Value on a failed result.");

    public static Result<T> Success(T value) => new(true, value);
    public static new Result<T> Failure(Error error) => new(false, default!, [error]);
    public static new Result<T> Failure(IEnumerable<Error> errors) => new(false, default!, errors);

    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        IsFailure ? Result<TOut>.Failure(Errors) : Result<TOut>.Success(mapper(Value));

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> binder) =>
        IsFailure ? Result<TOut>.Failure(Errors) : binder(Value);
}

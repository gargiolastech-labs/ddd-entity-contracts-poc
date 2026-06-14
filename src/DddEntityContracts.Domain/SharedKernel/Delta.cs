namespace Domain.SharedKernel;

// Property named IsChanged (not Changed) to avoid CS0102 with the static factory method Changed(T?).
public readonly record struct Delta<T>(T? Value, bool IsChanged)
{
    public static Delta<T> Changed(T? value) => new(value, true);
    public static Delta<T> Unchanged(T? value) => new(value, false);

    // null is a legitimate value: EqualityComparer is the source of truth, not null-check.
    public static Delta<T> From(T? current, T? next)
    {
        bool equal = EqualityComparer<T?>.Default.Equals(current, next);
        return equal ? Unchanged(current) : Changed(next);
    }
}

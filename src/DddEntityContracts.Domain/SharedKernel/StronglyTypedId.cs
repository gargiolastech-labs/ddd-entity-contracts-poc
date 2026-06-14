namespace Domain.SharedKernel;

// record struct cannot be inherited: each domain ID replicates this shape with its own name.
// See docs/conventions/value-object-authoring.md for the recommended pattern.
public readonly record struct StronglyTypedId(Guid Value)
{
    public static StronglyTypedId New() => new(Guid.NewGuid());
    public static StronglyTypedId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

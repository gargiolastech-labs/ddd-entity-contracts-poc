using System.Reflection;

namespace Domain.SharedKernel;

public sealed class FieldMap<TState>
{
    private readonly List<(string Name, Func<TState, TState, bool> Equal)> _fields = new();
    private bool _sealed;

    // Fix 1+3 (mutability): Track throws after Seal(), preventing post-init mutations.
    // Fix 2 (boxing): EqualityComparer<TField>.Default is captured before type erasure,
    //   so struct IEquatable<T> is dispatched correctly without boxing.
    public FieldMap<TState> Track<TField>(string name, Func<TState, TField> get)
    {
        if (_sealed)
            throw new InvalidOperationException($"FieldMap<{typeof(TState).Name}> is sealed.");
        var cmp = EqualityComparer<TField>.Default;
        _fields.Add((name, (a, b) => cmp.Equals(get(a), get(b))));
        return this;
    }

    // Fix 1 (exhaustiveness): reflects all public properties of TState and throws at
    //   class-load time if any are untracked, surfacing the omission in the test suite.
    public FieldMap<TState> Seal()
    {
        var trackedNames = _fields.Select(f => f.Name).ToHashSet();
        var untracked = typeof(TState)
            .GetProperties()
            .Select(p => p.Name)
            .Where(n => !trackedNames.Contains(n))
            .ToList();

        if (untracked.Count > 0)
            throw new InvalidOperationException(
                $"FieldMap<{typeof(TState).Name}> missing Track calls for: {string.Join(", ", untracked)}.");

        _sealed = true;
        return this;
    }

    // Fix 5 (allocation): List<string> is only allocated when at least one field changes;
    //   the no-change path (dominant in idempotent calls) returns the empty singleton.
    public IReadOnlyList<string> Diff(TState current, TState next)
    {
        List<string>? changed = null;
        foreach (var (name, equal) in _fields)
            if (!equal(current, next))
                (changed ??= new List<string>(_fields.Count)).Add(name);
        return changed ?? [];
    }
}

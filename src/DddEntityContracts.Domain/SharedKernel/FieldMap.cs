namespace Domain.SharedKernel;

public sealed class FieldMap<TState>
{
    private readonly List<(string Name, Func<TState, object?> Get)> _fields = new();

    public FieldMap<TState> Track<TField>(string name, Func<TState, TField> get)
    {
        _fields.Add((name, s => get(s)));
        return this;
    }

    public IReadOnlyList<string> Diff(TState current, TState next)
    {
        var changed = new List<string>(_fields.Count);
        foreach (var (name, get) in _fields)
            if (!Equals(get(current), get(next)))
                changed.Add(name);
        return changed;
    }
}

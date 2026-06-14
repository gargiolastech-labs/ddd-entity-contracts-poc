namespace Domain.SharedKernel;

public interface IDomainEvent
{
}

public interface IDomainCreatable<TSelf, in TRequest>
    where TSelf : IDomainCreatable<TSelf, TRequest>
{
    static abstract Result<TSelf> Create(TRequest request);
}

public interface IDomainUpdatable<in TRequest>
{
    Result Update(TRequest request);
}

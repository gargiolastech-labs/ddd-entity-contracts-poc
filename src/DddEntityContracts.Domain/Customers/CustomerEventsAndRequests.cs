using Domain.SharedKernel;

namespace Domain.Customers;

public sealed record CreateCustomerRequest(string? Name, string? Email, string? Phone);
public sealed record UpdateCustomerRequest(string? Name, string? Email, string? Phone);

public sealed record CustomerCreated(CustomerId CustomerId) : IDomainEvent;
public sealed record CustomerUpdated(CustomerId CustomerId, IReadOnlyCollection<string> ChangedFields) : IDomainEvent;

public sealed record CustomerActivated(CustomerId CustomerId) : IDomainEvent;
public sealed record CustomerDeactivated(CustomerId CustomerId, string? Reason) : IDomainEvent;

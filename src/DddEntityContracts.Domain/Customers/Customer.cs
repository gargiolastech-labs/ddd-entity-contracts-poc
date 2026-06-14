using Domain.SharedKernel;

namespace Domain.Customers;

public sealed class Customer : Entity<CustomerId>, IDomainCreatable<Customer, CreateCustomerRequest>
{
    public CustomerName Name { get; private set; } = default!;
    public Email Email { get; private set; } = default!;
    public PhoneNumber Phone { get; private set; } = default!;

    private Customer(CustomerId id) : base(id) { }

    public static Result<Customer> Create(CreateCustomerRequest request)
    {
        return BuildValidatedState(request)
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult())
            .Map(state =>
            {
                var id = CustomerId.New();
                var customer = new Customer(id);
                customer.Apply(state);
                customer.Raise(new CustomerCreated(id));
                return customer;
            });
    }

    private static Validation<ValidatedState> BuildValidatedState(CreateCustomerRequest request)
    {
        var vName  = CustomerName.Create(request.Name);
        var vEmail = Email.Create(request.Email);
        var vPhone = PhoneNumber.Create(request.Phone);

        return Validation<ValidatedState>.Combine(
            vName, vEmail, vPhone,
            (name, email, phone) => new ValidatedState(name, email, phone));
    }

    private static Validation<ValidatedState> CheckCrossFieldInvariants(ValidatedState state)
        => Validation<ValidatedState>.Success(state);

    private void Apply(ValidatedState state)
    {
        Name  = state.Name;
        Email = state.Email;
        Phone = state.Phone;
    }

    private sealed record ValidatedState(CustomerName Name, Email Email, PhoneNumber Phone);
}

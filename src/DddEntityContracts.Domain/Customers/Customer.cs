using Domain.SharedKernel;

namespace Domain.Customers;

public sealed class Customer :
    Entity<CustomerId>,
    IDomainCreatable<Customer, CreateCustomerRequest>,
    IDomainUpdatable<UpdateCustomerRequest>
{
    public CustomerName Name { get; private set; } = default!;
    public Email Email { get; private set; } = default!;
    public PhoneNumber Phone { get; private set; } = default!;
    public CustomerStatus Status { get; private set; } = CustomerStatus.Active;

    private Customer(CustomerId id) : base(id) { }

    // --- Create ---

    public static Result<Customer> Create(CreateCustomerRequest request) =>
        ValidateState(new CustomerStateInput(request.Name, request.Email, request.Phone))
            .Map(state =>
            {
                var id = CustomerId.New();
                var customer = new Customer(id);
                customer.Apply(state);
                customer.Raise(new CustomerCreated(id));
                return customer;
            });

    // --- Update (generic) ---

    public Result Update(UpdateCustomerRequest request) =>
        ApplyValidatedUpdate(new CustomerStateInput(request.Name, request.Email, request.Phone));

    // --- Lifecycle ---

    public Result Activate()
    {
        if (Status == CustomerStatus.Active)
            return Result.Success();

        Status = CustomerStatus.Active;
        Raise(new CustomerActivated(Id));
        return Result.Success();
    }

    public Result Deactivate(string? reason = null)
    {
        if (Status == CustomerStatus.Inactive)
            return Result.Success();

        Status = CustomerStatus.Inactive;
        Raise(new CustomerDeactivated(Id, reason));
        return Result.Success();
    }

    // --- Targeted edits ---

    public Result ChangeEmail(string? email) =>
        ApplyValidatedUpdate(new CustomerStateInput(Name.Value, email, Phone.Value));

    public Result ChangePhone(string? phone) =>
        ApplyValidatedUpdate(new CustomerStateInput(Name.Value, Email.Value, phone));

    // --- Shared seam ---

    private static readonly FieldMap<ValidatedState> Fields = new FieldMap<ValidatedState>()
        .Track(nameof(ValidatedState.Name),  s => s.Name)
        .Track(nameof(ValidatedState.Email), s => s.Email)
        .Track(nameof(ValidatedState.Phone), s => s.Phone);

    private ValidatedState Snapshot() => new(Name, Email, Phone);

    private Result ApplyValidatedUpdate(CustomerStateInput input)
    {
        var validation = ValidateState(input);
        if (validation.IsFailure) return Result.Failure(validation.Errors);

        var next = validation.Value;
        var changed = Fields.Diff(Snapshot(), next);
        if (changed.Count == 0) return Result.Success();

        Apply(next);
        Raise(new CustomerUpdated(Id, changed));
        return Result.Success();
    }

    private static Result<ValidatedState> ValidateState(CustomerStateInput input)
    {
        var vName  = CustomerName.Create(input.Name);
        var vEmail = Email.Create(input.Email);
        var vPhone = PhoneNumber.Create(input.Phone);

        return Validation<ValidatedState>.Combine(
                vName, vEmail, vPhone,
                (name, email, phone) => new ValidatedState(name, email, phone))
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult());
    }

    // An @internal.local email is a corporate address that requires a direct phone number on file.
    private static Validation<ValidatedState> CheckCrossFieldInvariants(ValidatedState state)
    {
        if (state.Email.Value.EndsWith("@internal.local", StringComparison.OrdinalIgnoreCase)
            && state.Phone.Value is null)
            return Validation<ValidatedState>.Failure(
                new Error("Customer.InternalEmailRequiresPhone",
                    "An internal email address requires a phone number."));

        return Validation<ValidatedState>.Success(state);
    }

    private void Apply(ValidatedState state)
    {
        Name  = state.Name;
        Email = state.Email;
        Phone = state.Phone;
    }

    // --- Nested types ---

    private sealed record CustomerStateInput(string? Name, string? Email, string? Phone);

    private sealed record ValidatedState(CustomerName Name, Email Email, PhoneNumber Phone);
}

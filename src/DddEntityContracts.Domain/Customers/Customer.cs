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

    // --- Update (generic) ---

    public Result Update(UpdateCustomerRequest request)
    {
        var validation = BuildValidatedState(request)
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult());

        if (validation.IsFailure)
            return Result.Failure(validation.Errors);

        var changes = ChangeSet.Diff(this, validation.Value);
        if (!changes.HasChanges)
            return Result.Success();

        changes.ApplyTo(this);
        Raise(new CustomerUpdated(Id, changes.ChangedFields));
        return Result.Success();
    }

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

    public Result ChangeEmail(string? email)
    {
        var validation = BuildValidatedState(new CustomerStateInput(Name.Value, email, Phone.Value))
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult());

        if (validation.IsFailure)
            return Result.Failure(validation.Errors);

        var delta = Delta<Email>.From(this.Email, validation.Value.Email);
        if (!delta.IsChanged)
            return Result.Success();

        this.Email = delta.Value!;
        Raise(new CustomerUpdated(Id, new[] { "Email" }));
        return Result.Success();
    }

    public Result ChangePhone(string? phone)
    {
        var validation = BuildValidatedState(new CustomerStateInput(Name.Value, Email.Value, phone))
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult());

        if (validation.IsFailure)
            return Result.Failure(validation.Errors);

        var delta = Delta<PhoneNumber>.From(Phone, validation.Value.Phone);
        if (!delta.IsChanged)
            return Result.Success();

        Phone = delta.Value!;
        Raise(new CustomerUpdated(Id, new[] { "Phone" }));
        return Result.Success();
    }

    // --- Shared seam ---

    private static Validation<ValidatedState> BuildValidatedState(CreateCustomerRequest request)
        => BuildValidatedState(new CustomerStateInput(request.Name, request.Email, request.Phone));

    private static Validation<ValidatedState> BuildValidatedState(UpdateCustomerRequest request)
        => BuildValidatedState(new CustomerStateInput(request.Name, request.Email, request.Phone));

    private static Validation<ValidatedState> BuildValidatedState(CustomerStateInput input)
    {
        var vName  = CustomerName.Create(input.Name);
        var vEmail = Email.Create(input.Email);
        var vPhone = PhoneNumber.Create(input.Phone);

        return Validation<ValidatedState>.Combine(
            vName, vEmail, vPhone,
            (name, email, phone) => new ValidatedState(name, email, phone));
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

    private sealed record ChangeSet(
        Delta<CustomerName> Name,
        Delta<Email> Email,
        Delta<PhoneNumber> Phone)
    {
        public bool HasChanges => Name.IsChanged || Email.IsChanged || Phone.IsChanged;

        public IReadOnlyCollection<string> ChangedFields
        {
            get
            {
                var fields = new List<string>(3);
                if (Name.IsChanged)  fields.Add(nameof(Name));
                if (Email.IsChanged) fields.Add(nameof(Email));
                if (Phone.IsChanged) fields.Add(nameof(Phone));
                return fields.AsReadOnly();
            }
        }

        public static ChangeSet Diff(Customer current, ValidatedState next) => new(
            Delta<CustomerName>.From(current.Name, next.Name),
            Delta<Email>.From(current.Email, next.Email),
            Delta<PhoneNumber>.From(current.Phone, next.Phone));

        public void ApplyTo(Customer customer)
        {
            if (Name.IsChanged)  customer.Name  = Name.Value!;
            if (Email.IsChanged) customer.Email = Email.Value!;
            if (Phone.IsChanged) customer.Phone = Phone.Value!;
        }
    }
}

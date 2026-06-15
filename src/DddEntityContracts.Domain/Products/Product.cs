using Domain.SharedKernel;

namespace Domain.Products;

public sealed class Product :
    Entity<ProductId>,
    IDomainCreatable<Product, CreateProductRequest>,
    IDomainUpdatable<UpdateProductRequest>
{
    public Sku Sku                       { get; private set; } = default!;
    public ProductName Name              { get; private set; } = default!;
    public ProductDescription Description { get; private set; } = default!;
    public Money Price                   { get; private set; } = default!;
    public ProductStatus Status          { get; private set; } = ProductStatus.Draft;

    private Product(ProductId id) : base(id) { }

    // ── Create ────────────────────────────────────────────────────────────

    public static Result<Product> Create(CreateProductRequest request)
    {
        return BuildValidatedState(request)
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult())
            .Map(state =>
            {
                var id = ProductId.New();
                var product = new Product(id);
                product.Apply(state);
                product.Raise(new ProductCreated(id));
                return product;
            });
    }

    // ── Update ────────────────────────────────────────────────────────────

    public Result Update(UpdateProductRequest request)
    {
        if (Status != ProductStatus.Draft)
            return Result.Failure(
                new Error("Product.UpdateNotAllowed",
                    "Product attributes can only be updated while in Draft status."));

        var validation = BuildValidatedState(request)
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult());

        if (validation.IsFailure)
            return Result.Failure(validation.Errors);

        var changes = ChangeSet.Diff(this, validation.Value);
        if (!changes.HasChanges)
            return Result.Success();

        changes.ApplyTo(this);
        Raise(new ProductUpdated(Id, changes.ChangedFields));
        return Result.Success();
    }

    // ── Behavioral operations ─────────────────────────────────────────────

    public Result Publish()
    {
        if (Status == ProductStatus.Published)
            return Result.Success();    // idempotent

        if (Status != ProductStatus.Draft)
            return Result.Failure(
                new Error("Product.PublishNotAllowed",
                    "Only products in Draft status can be published."));

        if (Description.Value is null)
            return Result.Failure(
                new Error("Product.PublishRequiresDescription",
                    "A published product must have a description."));

        Status = ProductStatus.Published;
        Raise(new ProductPublished(Id));
        return Result.Success();
    }

    public Result Archive(string? reason = null)
    {
        if (Status == ProductStatus.Archived)
            return Result.Success();    // idempotent

        if (Status != ProductStatus.Published)
            return Result.Failure(
                new Error("Product.ArchiveNotAllowed",
                    "Only published products can be archived."));

        Status = ProductStatus.Archived;
        Raise(new ProductArchived(Id, reason));
        return Result.Success();
    }

    // ── Targeted edits ────────────────────────────────────────────────────

    public Result ChangePrice(decimal? amount, string? currency)
    {
        if (Status != ProductStatus.Draft)
            return Result.Failure(
                new Error("Product.PriceChangeNotAllowed",
                    "Price can only be changed while in Draft status."));

        var validation = BuildValidatedState(new ProductStateInput(
                Sku.Value, Name.Value, Description.Value, amount, currency))
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult());

        if (validation.IsFailure)
            return Result.Failure(validation.Errors);

        var delta = Delta<Money>.From(Price, validation.Value.Price);
        if (!delta.IsChanged)
            return Result.Success();

        Price = delta.Value!;
        Raise(new ProductUpdated(Id, new[] { "Price" }));
        return Result.Success();
    }

    // ── Shared seam ───────────────────────────────────────────────────────

    private static Validation<ValidatedState> BuildValidatedState(CreateProductRequest r)
        => BuildValidatedState(new ProductStateInput(
            r.Sku, r.Name, r.Description, r.PriceAmount, r.PriceCurrency));

    private static Validation<ValidatedState> BuildValidatedState(UpdateProductRequest r)
        => BuildValidatedState(new ProductStateInput(
            r.Sku, r.Name, r.Description, r.PriceAmount, r.PriceCurrency));

    private static Validation<ValidatedState> BuildValidatedState(ProductStateInput input)
    {
        var vSku         = Sku.Create(input.Sku);
        var vName        = ProductName.Create(input.Name);
        var vDescription = ProductDescription.Create(input.Description);
        var vPrice       = Money.Create(input.PriceAmount, input.PriceCurrency);

        // Combine supports up to 3 arguments natively. Compose via nested Combine
        // to accumulate errors from all four fields without short-circuiting.
        var vNamePrice = Validation<(ProductName, Money)>.Combine(
            vName, vPrice, (n, p) => (n, p));

        var vSkuDescription = Validation<(Sku, ProductDescription)>.Combine(
            vSku, vDescription, (s, d) => (s, d));

        return Validation<ValidatedState>.Combine(
            vSkuDescription, vNamePrice,
            (sd, np) => new ValidatedState(sd.Item1, np.Item1, sd.Item2, np.Item2));
    }

    // Cross-field invariant: premium products (price > 1000) must carry a description.
    private static Validation<ValidatedState> CheckCrossFieldInvariants(ValidatedState state)
    {
        if (state.Price.Amount > 1000m && state.Description.Value is null)
            return Validation<ValidatedState>.Failure(
                new Error("Product.PremiumRequiresDescription",
                    "Products priced over 1000 require a description."));

        return Validation<ValidatedState>.Success(state);
    }

    private void Apply(ValidatedState state)
    {
        Sku         = state.Sku;
        Name        = state.Name;
        Description = state.Description;
        Price       = state.Price;
    }

    // ── Nested private types ──────────────────────────────────────────────

    private sealed record ProductStateInput(
        string? Sku,
        string? Name,
        string? Description,
        decimal? PriceAmount,
        string? PriceCurrency);

    private sealed record ValidatedState(
        Sku Sku,
        ProductName Name,
        ProductDescription Description,
        Money Price);

    private sealed record ChangeSet(
        Delta<Sku> Sku,
        Delta<ProductName> Name,
        Delta<ProductDescription> Description,
        Delta<Money> Price)
    {
        public bool HasChanges =>
            Sku.IsChanged || Name.IsChanged || Description.IsChanged || Price.IsChanged;

        public IReadOnlyCollection<string> ChangedFields
        {
            get
            {
                var fields = new List<string>(4);
                if (Sku.IsChanged)         fields.Add(nameof(Sku));
                if (Name.IsChanged)        fields.Add(nameof(Name));
                if (Description.IsChanged) fields.Add(nameof(Description));
                if (Price.IsChanged)       fields.Add(nameof(Price));
                return fields.AsReadOnly();
            }
        }

        public static ChangeSet Diff(Product current, ValidatedState next) => new(
            Delta<Sku>.From(current.Sku, next.Sku),
            Delta<ProductName>.From(current.Name, next.Name),
            Delta<ProductDescription>.From(current.Description, next.Description),
            Delta<Money>.From(current.Price, next.Price));

        public void ApplyTo(Product product)
        {
            if (Sku.IsChanged)         product.Sku         = Sku.Value!;
            if (Name.IsChanged)        product.Name        = Name.Value!;
            if (Description.IsChanged) product.Description = Description.Value!;
            if (Price.IsChanged)       product.Price       = Price.Value!;
        }
    }
}

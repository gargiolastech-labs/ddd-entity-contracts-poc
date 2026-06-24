using Domain.SharedKernel;

namespace Domain.Products;

public sealed class Product :
    Entity<ProductId>,
    IDomainCreatable<Product, CreateProductRequest>,
    IDomainUpdatable<UpdateProductRequest>
{
    public Sku Sku                        { get; private set; } = default!;
    public ProductName Name               { get; private set; } = default!;
    public ProductDescription Description { get; private set; } = default!;
    public Money Price                    { get; private set; } = default!;
    public ProductStatus Status           { get; private set; } = ProductStatus.Draft;

    private Product(ProductId id) : base(id) { }

    // ── Create ────────────────────────────────────────────────────────────

    public static Result<Product> Create(CreateProductRequest request) =>
        ValidateState(new ProductStateInput(
                request.Sku, request.Name, request.Description, request.PriceAmount, request.PriceCurrency))
            .Map(state =>
            {
                var id = ProductId.New();
                var product = new Product(id);
                product.Apply(state);
                product.Raise(new ProductCreated(id));
                return product;
            });

    // ── Update ────────────────────────────────────────────────────────────

    public Result Update(UpdateProductRequest request)
    {
        if (Status != ProductStatus.Draft)
            return Result.Failure(
                new Error("Product.UpdateNotAllowed",
                    "Product attributes can only be updated while in Draft status."));

        return ApplyValidatedUpdate(new ProductStateInput(
            request.Sku, request.Name, request.Description, request.PriceAmount, request.PriceCurrency));
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

        return ApplyValidatedUpdate(new ProductStateInput(
            Sku.Value, Name.Value, Description.Value, amount, currency));
    }

    // ── Shared seam ───────────────────────────────────────────────────────

    private static readonly FieldMap<ValidatedState> Fields = new FieldMap<ValidatedState>()
        .Track(nameof(ValidatedState.Sku),         s => s.Sku)
        .Track(nameof(ValidatedState.Name),        s => s.Name)
        .Track(nameof(ValidatedState.Description), s => s.Description)
        .Track(nameof(ValidatedState.Price),       s => s.Price)
        .Seal();

    private ValidatedState Snapshot() => new(Sku, Name, Description, Price);

    private Result ApplyValidatedUpdate(ProductStateInput input)
    {
        var validation = ValidateState(input);
        if (validation.IsFailure) return Result.Failure(validation.Errors);

        var next = validation.Value;
        var changed = Fields.Diff(Snapshot(), next);
        if (changed.Count == 0) return Result.Success();

        Apply(next);
        Raise(new ProductUpdated(Id, changed));
        return Result.Success();
    }

    private static Result<ValidatedState> ValidateState(ProductStateInput input)
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
                (sd, np) => new ValidatedState(sd.Item1, np.Item1, sd.Item2, np.Item2))
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult());
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
}

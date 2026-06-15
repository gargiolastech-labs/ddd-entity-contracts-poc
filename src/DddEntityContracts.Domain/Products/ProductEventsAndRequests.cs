using Domain.SharedKernel;

namespace Domain.Products;

// ── Requests ─────────────────────────────────────────────────────────────

public sealed record CreateProductRequest(
    string? Sku,
    string? Name,
    string? Description,
    decimal? PriceAmount,
    string? PriceCurrency);

public sealed record UpdateProductRequest(
    string? Sku,
    string? Name,
    string? Description,
    decimal? PriceAmount,
    string? PriceCurrency);

// ── Domain events ────────────────────────────────────────────────────────

public sealed record ProductCreated(ProductId ProductId) : IDomainEvent;

public sealed record ProductUpdated(
    ProductId ProductId,
    IReadOnlyCollection<string> ChangedFields) : IDomainEvent;

public sealed record ProductPublished(ProductId ProductId) : IDomainEvent;

public sealed record ProductArchived(ProductId ProductId, string? Reason) : IDomainEvent;

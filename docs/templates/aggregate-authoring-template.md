# Aggregate Authoring Template

## Purpose

This template provides a copy-ready skeleton for authoring new aggregates in this domain.
It encodes the **Decide → Apply** convention defined in
[ADR-001](../adr/ADR-001-domain-create-update-convention.md) and demonstrated by the
`Customer` pilot aggregate.

Follow this template when introducing a new aggregate. Adapt only what is specific to
your domain concept; do not deviate from the structural rules without updating the ADR.

---

## When to use this template

Use this template when:

- introducing a new aggregate root in the domain;
- adding `Create` to an existing entity that lacks it;
- adding `Update` to an existing aggregate;
- adding behavioral operations to an existing aggregate.

Do **not** use this template for:

- read models or projections;
- Value Objects (see [Value Object Authoring](../conventions/value-object-authoring.md));
- sagas or process managers.

---

## Aggregate skeleton

Replace every `AggregateName`, `AggregateId`, `FieldType`, and `FieldName` placeholder
with your domain-specific names. Delete sections that do not apply.

```csharp
using Domain.SharedKernel;

namespace Domain.YourContext;

public sealed class AggregateName :
    Entity<AggregateId>,
    IDomainCreatable<AggregateName, CreateAggregateRequest>,
    IDomainUpdatable<UpdateAggregateRequest>
{
    // ── Public properties (private setters) ──────────────────────────────────

    public FieldTypeA FieldA { get; private set; } = default!;
    public FieldTypeB FieldB { get; private set; } = default!;
    public AggregateStatus Status { get; private set; } = AggregateStatus.Active;

    // ── Private constructor ───────────────────────────────────────────────────

    private AggregateName(AggregateId id) : base(id) { }

    // ── Create ────────────────────────────────────────────────────────────────

    public static Result<AggregateName> Create(CreateAggregateRequest request)
    {
        return BuildValidatedState(request)
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult())
            .Map(state =>
            {
                var id = AggregateId.New();
                var aggregate = new AggregateName(id);
                aggregate.Apply(state);
                aggregate.Raise(new AggregateCreated(id));
                return aggregate;
            });
    }

    // ── Generic Update ────────────────────────────────────────────────────────

    public Result Update(UpdateAggregateRequest request)
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
        Raise(new AggregateUpdated(Id, changes.ChangedFields));
        return Result.Success();
    }

    // ── Behavioral operations ─────────────────────────────────────────────────

    public Result Activate()
    {
        if (Status == AggregateStatus.Active)
            return Result.Success();     // idempotent

        Status = AggregateStatus.Active;
        Raise(new AggregateActivated(Id));
        return Result.Success();
    }

    public Result Deactivate(string? reason = null)
    {
        if (Status == AggregateStatus.Inactive)
            return Result.Success();     // idempotent

        Status = AggregateStatus.Inactive;
        Raise(new AggregateDeactivated(Id, reason));
        return Result.Success();
    }

    // ── Targeted edits ────────────────────────────────────────────────────────

    public Result ChangeFieldA(string? fieldA)
    {
        // Build full prospective state (not just FieldA) to re-evaluate cross-field invariants.
        var validation = BuildValidatedState(new AggregateStateInput(fieldA, FieldB.Value))
            .ToResult()
            .Bind(state => CheckCrossFieldInvariants(state).ToResult());

        if (validation.IsFailure)
            return Result.Failure(validation.Errors);

        var delta = Delta<FieldTypeA>.From(FieldA, validation.Value.FieldA);
        if (!delta.IsChanged)
            return Result.Success();     // idempotent

        FieldA = delta.Value!;
        Raise(new AggregateUpdated(Id, new[] { "FieldA" }));
        return Result.Success();
    }

    // ── Shared seam ───────────────────────────────────────────────────────────

    private static Validation<ValidatedState> BuildValidatedState(CreateAggregateRequest r)
        => BuildValidatedState(new AggregateStateInput(r.FieldA, r.FieldB));

    private static Validation<ValidatedState> BuildValidatedState(UpdateAggregateRequest r)
        => BuildValidatedState(new AggregateStateInput(r.FieldA, r.FieldB));

    private static Validation<ValidatedState> BuildValidatedState(AggregateStateInput input)
    {
        var vFieldA = FieldTypeA.Create(input.FieldA);
        var vFieldB = FieldTypeB.Create(input.FieldB);

        return Validation<ValidatedState>.Combine(
            vFieldA, vFieldB,
            (fieldA, fieldB) => new ValidatedState(fieldA, fieldB));
    }

    // Add your cross-field invariants here. Evaluate the full prospective state,
    // never individual deltas.
    private static Validation<ValidatedState> CheckCrossFieldInvariants(ValidatedState state)
    {
        // Example: FieldA and FieldB cannot be identical
        // if (state.FieldA.Value == state.FieldB.Value)
        //     return Validation<ValidatedState>.Failure(
        //         new Error("Aggregate.SameFields", "FieldA and FieldB cannot be equal."));

        return Validation<ValidatedState>.Success(state);
    }

    private void Apply(ValidatedState state)
    {
        FieldA = state.FieldA;
        FieldB = state.FieldB;
    }

    // ── Nested private types ──────────────────────────────────────────────────

    // Shared raw input for all BuildValidatedState overloads.
    private sealed record AggregateStateInput(string? FieldA, string? FieldB);

    // The fully-validated prospective state. Private and concrete — never generic.
    private sealed record ValidatedState(FieldTypeA FieldA, FieldTypeB FieldB);

    // Tracks which fields changed between current and prospective state.
    private sealed record ChangeSet(
        Delta<FieldTypeA> FieldA,
        Delta<FieldTypeB> FieldB)
    {
        public bool HasChanges => FieldA.IsChanged || FieldB.IsChanged;

        public IReadOnlyCollection<string> ChangedFields
        {
            get
            {
                var fields = new List<string>(2);
                if (FieldA.IsChanged) fields.Add(nameof(FieldA));
                if (FieldB.IsChanged) fields.Add(nameof(FieldB));
                return fields.AsReadOnly();
            }
        }

        public static ChangeSet Diff(AggregateName current, ValidatedState next) => new(
            Delta<FieldTypeA>.From(current.FieldA, next.FieldA),
            Delta<FieldTypeB>.From(current.FieldB, next.FieldB));

        public void ApplyTo(AggregateName aggregate)
        {
            if (FieldA.IsChanged) aggregate.FieldA = FieldA.Value!;
            if (FieldB.IsChanged) aggregate.FieldB = FieldB.Value!;
        }
    }
}
```

---

## Value Objects

Each editable field should be a Value Object with:

- a private constructor;
- a `static Create(string? raw)` method returning `Validation<TVO>`;
- value equality (use `sealed record` or `readonly record struct`);
- no public setters.

```csharp
public sealed record FieldTypeA
{
    public string Value { get; }

    private FieldTypeA(string value) { Value = value; }

    public static Validation<FieldTypeA> Create(string? raw)
    {
        var notEmpty = ValueObjectGuards.NotNullOrWhiteSpace(
            raw, "FieldTypeA.Required", "Field A is required.");
        if (notEmpty.IsFailure)
            return Validation<FieldTypeA>.Failure(notEmpty.ToResult().Errors);

        var value = notEmpty.ToResult().Value.Trim();
        // Add further guards (MaxLength, Matches, etc.) as needed.

        return Validation<FieldTypeA>.Success(new FieldTypeA(value));
    }
}
```

For strongly-typed IDs, copy the `CustomerId` pattern (`readonly record struct`):

```csharp
public readonly record struct AggregateId(Guid Value)
{
    public static AggregateId New() => new(Guid.NewGuid());
    public static AggregateId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
```

---

## Requests

Define one request object per operation. Keep them dumb data carriers — no logic.

```csharp
// Create: only attributes needed to initialize the aggregate.
// Do NOT include Status or lifecycle flags.
public sealed record CreateAggregateRequest(string? FieldA, string? FieldB);

// Update: the same editable attributes as Create.
// Do NOT include Status or lifecycle flags.
public sealed record UpdateAggregateRequest(string? FieldA, string? FieldB);
```

---

## Events

Define one event per meaningful state transition.

```csharp
// Creation
public sealed record AggregateCreated(AggregateId AggregateId) : IDomainEvent;

// Mechanical update (generic update and targeted edits)
public sealed record AggregateUpdated(
    AggregateId AggregateId,
    IReadOnlyCollection<string> ChangedFields) : IDomainEvent;

// Behavioral / semantic events — intention-revealing
public sealed record AggregateActivated(AggregateId AggregateId) : IDomainEvent;
public sealed record AggregateDeactivated(AggregateId AggregateId, string? Reason) : IDomainEvent;
```

Rules:
- Do **not** introduce per-field events (e.g. `AggregateFieldAChanged`).
- Do **not** replace semantic events with a generic `AggregateStatusChanged`.
- `AggregateUpdated` is mechanical; behavioral events are semantic.

---

## ValidatedState

Keep `ValidatedState` as a **private, nested, concrete record**. Never extract it to a
shared or generic type.

```csharp
private sealed record ValidatedState(FieldTypeA FieldA, FieldTypeB FieldB);
```

What to customize:
- Add one positional parameter per editable field.
- Match the parameter types to your Value Object types.

What not to do:
- Do not add a `Status` field — lifecycle status is not part of `ValidatedState`.
- Do not make it `public`.
- Do not introduce `ValidatedState<T>`.

---

## BuildValidatedState

`BuildValidatedState` is the single entry point for all Decide phases.

Customization checklist:
- [ ] One `AggregateStateInput` parameter per editable field (`string?` types).
- [ ] One `VO.Create(input.Field)` call per field.
- [ ] One `Validation<ValidatedState>.Combine(...)` call — use the 2-arg, 3-arg, or
      extended overload as needed.
- [ ] Adapters for each operation type (Create, Update, targeted edits).
- [ ] No raw string assigned to a domain property anywhere in this method.

---

## CheckCrossFieldInvariants

The invariant hook is called after all Value Objects are constructed successfully.

Customization checklist:
- [ ] Evaluate on the **complete** `ValidatedState`, not on individual input values.
- [ ] Return `Validation<ValidatedState>.Failure(...)` for each invariant violation.
- [ ] Use `Validation<ValidatedState>.Combine(...)` if multiple independent invariants
      could fire simultaneously.
- [ ] Leave empty (return Success) if no cross-field invariants exist yet.

---

## Create

Checklist:
- [ ] Calls `BuildValidatedState(request)`.
- [ ] Chains `.ToResult().Bind(state => CheckCrossFieldInvariants(state).ToResult())`.
- [ ] On success: instantiates aggregate with `new(id)`, calls `Apply(state)`, raises `AggregateCreated`.
- [ ] On failure: no aggregate is constructed, no events are raised.
- [ ] The aggregate is initialized with the default active status — no status parameter in request.

---

## Update

Checklist:
- [ ] Calls `BuildValidatedState(request)` via the shared seam.
- [ ] Calls `CheckCrossFieldInvariants` on the full prospective state.
- [ ] On validation failure: returns `Result.Failure(errors)` — no mutation, no events.
- [ ] Computes `ChangeSet.Diff(this, validation.Value)`.
- [ ] If `!HasChanges`: returns `Result.Success()` — no mutation, no events.
- [ ] If `HasChanges`: calls `changes.ApplyTo(this)`, raises `AggregateUpdated(Id, ChangedFields)`.
- [ ] Does **not** modify `Status`.

---

## ChangeSet

Checklist:
- [ ] One `Delta<FieldType>` field per editable attribute.
- [ ] `HasChanges` property derived from individual `IsChanged` flags.
- [ ] `ChangedFields` derived from field names using `nameof`.
- [ ] `Diff(current, next)` factory uses `Delta<T>.From(current.Field, next.Field)`.
- [ ] `ApplyTo(aggregate)` guards every assignment with `if (Field.IsChanged)`.
- [ ] `ChangeSet` is `private sealed record` — nested inside the aggregate.

---

## Targeted edits

Checklist per operation (e.g. `ChangeFieldA`):
- [ ] Builds the **full** `AggregateStateInput` using current values for unchanged fields.
- [ ] Calls `BuildValidatedState` (not just the single VO's `Create`).
- [ ] Calls `CheckCrossFieldInvariants` on the full prospective state.
- [ ] Computes a single `Delta<T>` for the target field.
- [ ] Returns `Result.Success()` with no mutation if the value is unchanged (idempotent).
- [ ] Assigns the field from `delta.Value!` — not from the raw input.
- [ ] Raises `AggregateUpdated(Id, ["FieldName"])` or a semantic event if appropriate.

---

## Behavioral operations

Checklist per operation (e.g. `Activate`, `Deactivate`):
- [ ] Guard at the top: if already in target state → return `Result.Success()` (idempotent).
- [ ] Mutate `Status` directly (no `BuildValidatedState` unless attribute validation is needed).
- [ ] Raise a **semantic event** (`AggregateActivated` / `AggregateDeactivated`) — not `AggregateUpdated`.
- [ ] Pass contextual payload to event if meaningful (e.g. deactivation reason).
- [ ] `Status` setter is private.

---

## Tests checklist

### Create
- [ ] Valid request → success, fields populated, `AggregateCreated` raised.
- [ ] Missing required field → failure, all errors accumulated.
- [ ] Multiple invalid fields → all errors present (validates accumulation).
- [ ] Cross-field invariant violated → failure, correct error code.
- [ ] Status is `Active` after creation.

### Update (generic)
- [ ] No changes → `Result.Success()`, no mutation, no event.
- [ ] Single field changed → `AggregateUpdated` with that field only.
- [ ] Multiple fields changed → one `AggregateUpdated` with all changed fields.
- [ ] Validation failure → no mutation, no events (atomicity).
- [ ] Clearing nullable field to null → detected as change (`Delta<T>` null-safety).
- [ ] Cross-field invariant violated → failure, no mutation, no event.

### Behavioral operations
- [ ] Transition from default to target state → event emitted.
- [ ] Already in target state → `Result.Success()`, no event (idempotent).
- [ ] Status is not part of `UpdateAggregateRequest` (contract test via reflection in tests).

### Targeted edits
- [ ] Valid new value → field updated, `AggregateUpdated(["FieldName"])`.
- [ ] Invalid value → failure, no mutation, no event.
- [ ] Same value (possibly normalized) → no event (idempotent).
- [ ] Nullable field cleared to null → detected as change.
- [ ] Cross-field invariant re-evaluated on full state.

---

## Anti-patterns

| Anti-pattern | Problem | Correct approach |
|---|---|---|
| `public AggregateName(...)` constructor | Bypasses invariants enforced by `Create` | Private constructor only |
| `public FieldTypeA FieldA { get; set; }` | External code can set invalid state | `private set` always |
| `FieldA = request.FieldA` in `Create` or `Update` | Assigns raw input without validation | Always go through `FieldTypeA.Create(request.FieldA)` |
| Validating only changed fields in `Update` | Cross-field invariants may break on the unchanged side | Validate the full prospective state every time |
| `if (request.FieldA != null) FieldA = request.FieldA` in Apply | Conflates `null` (valid value) with "not provided" | Use `Delta<T>` with `IsChanged` |
| Raising `AggregateUpdated` from `Activate`/`Deactivate` | Hides domain intent — `Activated` means something specific | Use intention-revealing events |
| `Status` in `UpdateAggregateRequest` | Update should be attribute-only; lifecycle is behavioral | Status via `Activate`/`Deactivate` only |
| `Validation<T>.Bind(...)` chaining | `Validation<T>` has no `Bind` — mixing monadic and applicative styles | Use `Combine` for accumulation, `ToResult()` then `Bind` for sequencing |
| `ValidatedState<T>` generic | Premature abstraction — each aggregate has a different shape | Concrete nested record per aggregate |
| `ChangeSet<T>` generic | Same — field types and names are aggregate-specific | Concrete nested record per aggregate |
| `Value Object : SomeBase` inheritance for shared behaviour | `record` inheritance breaks value equality predictably | Composition via `ValueObjectGuards`; each VO is standalone |
| `typeof(...)` or `GetType()` in domain code | Reflection bypasses compile-time safety | Explicit type checks and dispatch |
| Emitting a domain event before the mutation | Event consumers read stale state | Mutate first, raise after |
| Emitting a domain event even when nothing changed | Spurious events pollute the event log | Guard with `if (delta.IsChanged)` or `if (changeSet.HasChanges)` |

# ADR-001 — Domain Create / Update Convention

## Status

Accepted

---

## Context

The PoC `ddd-entity-contracts-poc` demonstrates atomic entity creation and update in a
Domain-Driven Design context using .NET 9. Across STORY-05, STORY-06, and STORY-07
a recurring set of structural decisions emerged for the `Customer` aggregate:

- validation must accumulate all errors before mutating state;
- mutation must never happen during validation;
- domain events must only be raised when state actually changes;
- lifecycle transitions are behaviorally distinct from attribute updates;
- null is a legitimate value and must not be conflated with "no change".

These decisions were made incrementally. This ADR codifies them as a formal convention
that applies to all aggregates in the domain and serves as the authoritative input for
the static analyzer planned in phase 2.

---

## Decision

All aggregates in this domain follow the **Decide → Apply** pattern. Every creation and
mutation operation is split into two strictly non-overlapping phases:

1. **Decide** — pure validation that builds a `ValidatedState` without mutating the aggregate.
2. **Apply** — imperative mutation that trusts the validated state and raises domain events.

A single private seam, `BuildValidatedState`, is the unique entry point for all Decide
phases regardless of the operation type (Create, Update, or targeted edit).

---

## Consequences

**Benefits:**

- Validation is always atomic: the aggregate can never be in a partially-valid state.
- Mutation is always intentional: Apply only runs on a proven-valid ValidatedState.
- Error accumulation is built-in: callers always receive the full error set, not just the first.
- Idempotence is free: comparing current vs. prospective state via `Delta<T>` makes
  "nothing changed → no event" a structural guarantee, not a runtime check.
- The seam is testable in isolation: `BuildValidatedState` can be tested without
  constructing a full aggregate.

**Trade-offs:**

- Each aggregate carries its own `ValidatedState` and `ChangeSet` records. This is
  intentional — premature generics (`ValidatedState<T>`, `ChangeSet<T>`) are prohibited.
- Targeted edits must rebuild the full `ValidatedState` even when changing one field.
  This is the cost of re-evaluating cross-field invariants on the complete prospective state.

---

## Convention

### Entity construction

- Aggregate constructors are **always private**.
- No public constructor may bypass invariants.
- Domain properties have **private setters**.
- The aggregate is reachable only through factory methods (`Create`, etc.).

```csharp
public sealed class Customer : Entity<CustomerId>, ...
{
    public CustomerName Name { get; private set; } = default!;

    private Customer(CustomerId id) : base(id) { }
}
```

---

### Static Create

- Every aggregate exposes a **`static Create`** factory method.
- `Create` returns `Result<TAggregate>`.
- `Create` accepts a dedicated request object (never raw primitives directly).
- When the aggregate implements `IDomainCreatable<TSelf, TRequest>`, the signature
  satisfies the `static abstract` interface contract.

```csharp
public static Result<Customer> Create(CreateCustomerRequest request)
```

The aggregate is initialized `Active` (or default lifecycle state) by the constructor,
not by a parameter in the request.

---

### Result and Validation semantics

| Type | Semantics | Use |
|---|---|---|
| `Result<T>` | Monadic, short-circuits on first failure | Orchestrate Decide → Apply |
| `Validation<T>` | Applicative, accumulates all errors | Inside `BuildValidatedState` |
| `Validation<T>.Combine` | Parallel error accumulation | Multi-field validation |
| `ToResult()` | Converts accumulated errors to Result | At entity / use-case boundary |

Rules:
- `Map` and `Bind` live on `Result<T>` and short-circuit.
- `Combine` lives on `Validation<T>` and accumulates.
- `ToResult()` is called at the entity level to bridge the two; not inside VO factories.
- Value Object `Create` methods return `Validation<TVO>`, never `Result<TVO>`.

---

### Decide phase

The Decide phase:

- receives raw input (request object);
- constructs Value Objects via their `Create(...)` factory — never assigns raw strings;
- accumulates all errors using `Validation<T>.Combine`;
- evaluates cross-field invariants on the complete prospective state;
- builds a concrete `ValidatedState` private to the aggregate;
- **never mutates the aggregate**.

---

### BuildValidatedState seam

`BuildValidatedState` is the **single and mandatory seam** of the Decide phase.

Rules:
- One implementation, shared by `Create`, `Update`, and all targeted edits.
- Accepts a private `AggregateStateInput` record that abstracts over request types.
- Public-facing overloads (`CreateRequest`, `UpdateRequest`) delegate to the shared implementation.
- Never assigns raw input to aggregate properties.
- Remains concrete on the aggregate — never extracted to a generic base.

```csharp
// Private shared input record
private sealed record CustomerStateInput(string? Name, string? Email, string? Phone);

// Shared implementation
private static Validation<ValidatedState> BuildValidatedState(CustomerStateInput input)
{
    var vName  = CustomerName.Create(input.Name);
    var vEmail = Email.Create(input.Email);
    var vPhone = PhoneNumber.Create(input.Phone);

    return Validation<ValidatedState>.Combine(
        vName, vEmail, vPhone,
        (name, email, phone) => new ValidatedState(name, email, phone));
}

// Per-operation adapters
private static Validation<ValidatedState> BuildValidatedState(CreateCustomerRequest r)
    => BuildValidatedState(new CustomerStateInput(r.Name, r.Email, r.Phone));

private static Validation<ValidatedState> BuildValidatedState(UpdateCustomerRequest r)
    => BuildValidatedState(new CustomerStateInput(r.Name, r.Email, r.Phone));
```

---

### Cross-field invariants

- Evaluated **after** all Value Objects are successfully constructed.
- Evaluated on the **complete prospective state** (`ValidatedState`), never on individual deltas.
- Apply to `Create`, `Update`, and targeted edits equally.
- Implemented in a dedicated hook: `CheckCrossFieldInvariants(ValidatedState state)`.
- Return `Validation<ValidatedState>` — can accumulate multiple invariant violations.

```csharp
// Example: corporate email requires phone on file
private static Validation<ValidatedState> CheckCrossFieldInvariants(ValidatedState state)
{
    if (state.Email.Value.EndsWith("@internal.local", StringComparison.OrdinalIgnoreCase)
        && state.Phone.Value is null)
        return Validation<ValidatedState>.Failure(
            new Error("Customer.InternalEmailRequiresPhone",
                "An internal email address requires a phone number."));

    return Validation<ValidatedState>.Success(state);
}
```

---

### ValidatedState

- A **concrete, private, nested record** inside the aggregate.
- Represents the fully-validated prospective state before mutation.
- Never introduced as a generic `ValidatedState<T>` — each aggregate has its own shape.
- Passed from Decide to Apply; never exposed outside the aggregate.

```csharp
private sealed record ValidatedState(CustomerName Name, Email Email, PhoneNumber Phone);
```

---

### Apply phase

The Apply phase:

- runs **only after a successful Decide** (receives `ValidatedState`, never raw input);
- computes `Delta<T>` for each field by comparing current value with prospective value;
- **mutates only fields where `delta.IsChanged == true`**;
- **raises domain events only for fields that actually changed**;
- performs no validation of business rules.

```csharp
private void Apply(ValidatedState state)
{
    Name  = state.Name;
    Email = state.Email;
    Phone = state.Phone;
}
```

For Create, Apply is a straight assignment (no previous state exists).
For Update and targeted edits, Apply is mediated by `ChangeSet`.

---

### ChangeSet

`ChangeSet` tracks which fields changed between current and prospective state.

- **Private, nested, concrete record** inside the aggregate.
- Uses `Delta<T>` for each editable field.
- Never introduced as generic `ChangeSet<T>`.
- Exposes `HasChanges` and `ChangedFields` to drive event emission.
- `Diff(current, next)` factory compares via `Delta<T>.From(current.Field, next.Field)`.
- `ApplyTo(aggregate)` mutates only changed fields.

```csharp
private sealed record ChangeSet(
    Delta<CustomerName> Name,
    Delta<Email> Email,
    Delta<PhoneNumber> Phone)
{
    public bool HasChanges => Name.IsChanged || Email.IsChanged || Phone.IsChanged;

    public IReadOnlyCollection<string> ChangedFields { get { ... } }

    public static ChangeSet Diff(Customer current, ValidatedState next) => new(
        Delta<CustomerName>.From(current.Name, next.Name),
        Delta<Email>.From(current.Email, next.Email),
        Delta<PhoneNumber>.From(current.Phone, next.Phone));

    public void ApplyTo(Customer customer) { ... }
}
```

---

### Delta

`Delta<T>` is the primitive that represents an explicit change.

```csharp
public readonly record struct Delta<T>(T? Value, bool IsChanged)
```

Rules:
- `null` is a **legitimate value** — never use `null` to mean "unchanged".
- `IsChanged = true` with `Value = null` means "field cleared to absent".
- `Delta<T>.From(current, next)` uses `EqualityComparer<T?>.Default` — the source of truth.
- Always use `Delta<T>` for every field in Apply; never compare and assign directly.

```csharp
// CORRECT
var delta = Delta<Email>.From(currentEmail, state.Email);
if (delta.IsChanged) { Email = delta.Value!; Raise(...); }

// WRONG — null is ambiguous
if (state.Email != currentEmail) Email = state.Email;
```

---

### Generic update

The generic `Update` operation:

- performs a full-replace of **all editable attributes** in a single atomic operation;
- **does not manage lifecycle status** — status has its own behavioral operations;
- reuses `BuildValidatedState` and `CheckCrossFieldInvariants`;
- uses `ChangeSet.Diff` to compute actual changes;
- emits **one mechanical event** (`CustomerUpdated`) carrying the list of changed fields;
- emits no event if nothing changed.

```csharp
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
```

---

### Targeted edits

Targeted edits (`ChangeEmail`, `ChangePhone`, etc.):

- change **one attribute** with explicit intent;
- must reuse `BuildValidatedState` — build the full prospective state, not just the target field;
- must re-evaluate `CheckCrossFieldInvariants` on the full state;
- compute a single `Delta<T>` for the target field;
- are **idempotent**: same value → no mutation, no event;
- emit `CustomerUpdated(["FieldName"])` (mechanical) when the field changes, or a semantic
  event if the operation carries domain meaning beyond the attribute change.

```csharp
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
```

---

### Behavioral operations

Behavioral operations (`Activate`, `Deactivate`, etc.):

- represent **domain-meaningful transitions**, not attribute mutations;
- carry intention-revealing events (`CustomerActivated`, `CustomerDeactivated`);
- must **not** emit the mechanical `CustomerUpdated`;
- are **idempotent**: already in target state → `Result.Success()`, no mutation, no event;
- do not require `BuildValidatedState` unless they involve attribute validation;
- may carry a contextual payload (e.g. deactivation reason).

```csharp
public Result Deactivate(string? reason = null)
{
    if (Status == CustomerStatus.Inactive)
        return Result.Success();          // idempotent

    Status = CustomerStatus.Inactive;
    Raise(new CustomerDeactivated(Id, reason));
    return Result.Success();
}
```

---

### Domain events

| Event | Trigger | Notes |
|---|---|---|
| `CustomerCreated` | Successful `Create` | Always emitted on creation |
| `CustomerUpdated(ChangedFields)` | Generic update or targeted edit with at least one change | Never emitted if `HasChanges == false` |
| `CustomerActivated` | `Activate()` from Inactive | Semantic — not interchangeable with `CustomerUpdated` |
| `CustomerDeactivated(Reason)` | `Deactivate()` from Active | Semantic — carries optional reason |

Rules:
- No event is emitted if nothing changes.
- No event is emitted on validation failure.
- Per-field events (e.g. `CustomerEmailChanged`) are not introduced — use `CustomerUpdated(ChangedFields)`.
- Behavioral events are never replaced by a generic `CustomerStatusChanged`.

---

### Lifecycle status

- `CustomerStatus` is **not** a parameter in `CreateCustomerRequest` or `UpdateCustomerRequest`.
- The aggregate is always initialized to the default active status in `Create`.
- Status transitions happen exclusively via behavioral operations.
- The `Status` property has a **private setter**.

---

### What must not be abstracted yet

The following abstractions are **explicitly prohibited** until proven necessary by real duplication
across multiple aggregates:

| Prohibited abstraction | Reason |
|---|---|
| `ValidatedState<T>` | Each aggregate has its own shape; premature generic loses type safety |
| `ChangeSet<T>` | Same — field names and types are aggregate-specific |
| `MutationPlan<T>` | Over-engineering: Apply is simple enough inline |
| Generic aggregate update framework | The pattern is clear; a framework hides the invariants it should enforce |
| Source Generator for boilerplate | Not yet — the boilerplate is minimal and locally visible |
| Reflection in domain code | Never — reflection bypasses the type safety the pattern provides |

---

## Analyzer phase 2 input

The following diagnostics are planned for the Roslyn Analyzer (STORY-09+).

| Diagnostic ID | Title | Severity | Description | Static? | FP Risk |
|---|---|---|---|---|---|
| `DDD001` | Missing static Create method | Error | Aggregate implementing `IDomainCreatable` must expose `static Create` returning `Result<TAggregate>`. | Yes | Low |
| `DDD002` | Invalid Create return type | Error | `Create` must return `Result<TAggregate>`, not `Result`, `TAggregate`, or void. | Yes | Low |
| `DDD003` | Public constructor on aggregate | Error | Aggregate entities must not expose public constructors. | Yes | Low |
| `DDD004` | Public setter on domain property | Warning | Domain properties must not expose public setters. Lifecycle properties and ID are excluded. | Yes | Medium — value types may have legitimate public setters outside domain |
| `DDD005` | Missing BuildValidatedState seam | Warning | Aggregate with `Create` or `Update` should expose a private `BuildValidatedState` method. | Yes | Medium — heuristic based on method name |
| `DDD006` | Generic ValidatedState abstraction | Warning | `ValidatedState<T>` must not be introduced before proven cross-aggregate duplication. | Yes | Low |
| `DDD007` | Generic ChangeSet abstraction | Warning | `ChangeSet<T>` must not be introduced before proven cross-aggregate duplication. | Yes | Low |
| `DDD008` | Lifecycle status in update request | Error | The generic update request must not contain a `Status` property. | Yes | Low |
| `DDD009` | Behavioral operation emits generic update event | Warning | Behavioral operations (`Activate`, `Deactivate`, etc.) should emit semantic events, not `CustomerUpdated`. | Yes | High — requires naming heuristics |
| `DDD010` | Direct raw assignment from request | Warning | Aggregate properties must not be assigned directly from request raw values; use VO `Create`. | Yes | High — requires data-flow analysis |

Notes on static detectability:
- `DDD001`–`DDD003` are syntactic and reliable.
- `DDD004`–`DDD008` are syntactic with moderate heuristics.
- `DDD009`–`DDD010` require light semantic / data-flow analysis and carry higher false-positive risk;
  they should be opt-in or suppressed with justification.

---

## Examples

See the `Customer` aggregate (STORY-05 through STORY-07) for the reference implementation:

```
src/DddEntityContracts.Domain/Customers/Customer.cs
src/DddEntityContracts.Domain/Customers/CustomerValueObjects.cs
src/DddEntityContracts.Domain/Customers/CustomerEventsAndRequests.cs
```

And the authoritative tests:

```
tests/DddEntityContracts.Domain.Tests/Customers/CustomerCreationTests.cs
tests/DddEntityContracts.Domain.Tests/Customers/CustomerUpdateTests.cs
tests/DddEntityContracts.Domain.Tests/Customers/CustomerBehaviorTests.cs
tests/DddEntityContracts.Domain.Tests/Customers/CustomerTargetedEditTests.cs
```

---

## Non-goals

- This ADR does not define persistence mapping.
- This ADR does not define aggregate boundaries or bounded context design.
- This ADR does not cover event sourcing or event store integration.
- This ADR does not define saga / process manager patterns.
- This ADR does not define query-side (CQRS read model) conventions.
- The Analyzer implementation is deferred to a dedicated STORY.

---

## References

- [Value Object Authoring Convention](../conventions/value-object-authoring.md)
- [Decide / Apply Pattern Convention](../conventions/decide-apply.md)
- [Aggregate Authoring Template](../templates/aggregate-authoring-template.md)
- Vernon, V. — *Implementing Domain-Driven Design*, Chapter 7: Aggregates
- Fowler, M. — *Patterns of Enterprise Application Architecture*: Domain Model

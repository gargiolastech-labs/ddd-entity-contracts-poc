# 04 — Customer walkthrough

Questo documento percorre l'aggregate `Customer` end-to-end. È l'unico aggregate del PoC, ed è progettato come **esemplare**: tutto ciò che fa è una convenzione da replicare quando si aggiungono nuovi aggregate.

Tutti i file citati vivono in `src/DddEntityContracts.Domain/Customers/`.

## La forma dell'aggregate

```csharp
public sealed class Customer :
    Entity<CustomerId>,
    IDomainCreatable<Customer, CreateCustomerRequest>,
    IDomainUpdatable<UpdateCustomerRequest>
{
    public CustomerName Name      { get; private set; } = default!;
    public Email Email            { get; private set; } = default!;
    public PhoneNumber Phone      { get; private set; } = default!;
    public CustomerStatus Status  { get; private set; } = CustomerStatus.Active;

    private Customer(CustomerId id) : base(id) { }

    // ... operations ...
}
```

Punti chiave:

- `sealed`: nessuna sottoclasse possibile. Eredità inutile per un aggregate concreto.
- Setter `private`: nessun caller esterno può scrivere direttamente uno stato. Tutto passa per i metodi pubblici.
- Inizializzazione `default!`: i campi vengono valorizzati nel costruttore privato + `Apply()`. La nullable annotation rimane `non-null` perché dopo `Create()` lo stato è garantito valido.
- Costruttore `private`: l'unico modo per costruire un `Customer` è il factory statico `Create`.
- `CustomerStatus.Active` come default lifecycle.

## I Value Object

```csharp
public readonly record struct CustomerId(Guid Value) { /* ... */ }

public sealed record CustomerName { public string Value { get; } /* ... */ }
public sealed record Email        { public string Value { get; } /* ... */ }
public sealed record PhoneNumber  { public string? Value { get; } /* ... */ }

public enum CustomerStatus { Active = 1, Inactive = 2 }
```

| VO | Tipo | Regole di validazione |
|---|---|---|
| `CustomerId` | `readonly record struct` | `Guid.Empty` rifiutato da `From`, accettato da `New` (genera nuovo) |
| `CustomerName` | `sealed record` | non vuoto, trim, max 100 char |
| `Email` | `sealed record` | non vuoto, trim, lowercase, max 254 char, regex |
| `PhoneNumber` | `sealed record` | nullable (assenza è legittima); se presente: max 30 char, regex |

`PhoneNumber` è l'unico VO che può avere `Value == null`: significa "il customer non ha un telefono". Non è un errore di validazione.

### Pattern di validazione

`CustomerName.Create` mostra il pattern sequenziale (gli step dipendono l'uno dall'altro):

```csharp
public static Validation<CustomerName> Create(string? raw)
{
    var notEmpty = ValueObjectGuards.NotNullOrWhiteSpace(raw, "CustomerName.Required", ...);
    if (notEmpty.IsFailure)
        return Validation<CustomerName>.Failure(notEmpty.ToResult().Errors);

    var value = notEmpty.ToResult().Value.Trim();

    var maxLength = ValueObjectGuards.MaxLength(value, 100, "CustomerName.MaxLength", ...);
    if (maxLength.IsFailure)
        return Validation<CustomerName>.Failure(maxLength.ToResult().Errors);

    return Validation<CustomerName>.Success(new CustomerName(value));
}
```

`Email.Create` mostra il pattern parallelo (check indipendenti sul valore già normalizzato):

```csharp
var normalized = notEmpty.ToResult().Value.Trim().ToLowerInvariant();

var vMaxLength = ValueObjectGuards.MaxLength(normalized, 254, ...);
var vFormat    = ValueObjectGuards.Matches(normalized, EmailPattern, ...);

return Validation<Email>.Combine(vMaxLength, vFormat, (_, _) => new Email(normalized));
```

La scelta dipende dalla logica del VO. Non è uno style point.

## Gli eventi di dominio

```csharp
public sealed record CustomerCreated(CustomerId CustomerId) : IDomainEvent;
public sealed record CustomerUpdated(CustomerId CustomerId, IReadOnlyCollection<string> ChangedFields) : IDomainEvent;
public sealed record CustomerActivated(CustomerId CustomerId) : IDomainEvent;
public sealed record CustomerDeactivated(CustomerId CustomerId, string? Reason) : IDomainEvent;
```

Punti chiave:

- Ogni operazione che cambia lo stato in modo significativo ha **il proprio evento dedicato**. `Updated` non è sufficiente: `Activated` e `Deactivated` portano informazione semantica diversa (intention-revealing).
- `CustomerUpdated` porta i **campi cambiati** come collezione. Un consumer può dispatchare logica diversa se è cambiato l'email vs il nome.
- `CustomerDeactivated` porta una `Reason` opzionale: utile per audit/UI ma non obbligatoria nel dominio.
- Tutti i record implementano `IDomainEvent` (marker, no proprietà).

## Le request

```csharp
public sealed record CreateCustomerRequest(string? Name, string? Email, string? Phone);
public sealed record UpdateCustomerRequest(string? Name, string? Email, string? Phone);
```

Tutti i campi sono `string?`: il parsing/validazione è responsabilità del Domain, non del chiamante. Una request **non è uno stato valido**: è solo input grezzo da cui costruire (o aggiornare) uno stato.

`Create` e `Update` hanno la stessa forma. Questo non è un caso: la fase Decide condivide la stessa funzione `BuildValidatedState`.

## Le operazioni

### Create

```csharp
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
```

Quattro step:

1. `BuildValidatedState(request)` — accumula errori di tutti i VO.
2. `.ToResult()` — passa al mondo monadico.
3. `.Bind(state => CheckCrossFieldInvariants(state).ToResult())` — verifica invarianti cross-field (es. "email @internal.local richiede phone"). Short-circuit.
4. `.Map(...)` — costruisce l'aggregate, `Apply` lo stato, solleva `CustomerCreated`.

Se uno qualunque dei primi tre step fallisce, l'aggregate **non viene mai costruito**. Lo stato non è mai parzialmente valido.

### Update

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
        return Result.Success();   // ← idempotenza strutturale

    changes.ApplyTo(this);
    Raise(new CustomerUpdated(Id, changes.ChangedFields));
    return Result.Success();
}
```

Differenze chiave rispetto a `Create`:

- **Non costruisce un nuovo aggregate**: muta `this`.
- **Diff esplicito**: `ChangeSet.Diff(this, validatedState)` confronta lo stato corrente con quello validato.
- **No-op semanticamente corretto**: se nulla è cambiato, non viene sollevato alcun evento. È un `Result.Success` legittimo, non un fallimento.
- **`ChangedFields`** sull'evento: il consumer sa cosa è effettivamente cambiato.

### Activate / Deactivate

```csharp
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
```

Behavioral operations: cambiano una proprietà sola (`Status`) e sollevano un evento intention-revealing.

**Idempotenza**: se l'aggregate è già nello stato target, non solleva l'evento. Questa è la stessa filosofia di `Update` — niente cambia, niente evento.

`Activate` non accetta una reason. `Deactivate` sì, perché la business semantica delle due operazioni è diversa.

### ChangeEmail / ChangePhone

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

Operations mirate su un singolo attributo. Importante:

- **Riusa `BuildValidatedState`**: la validazione di Email non viene replicata; passa per lo stesso seam con gli altri campi invariati.
- **Validazione completa cross-field**: anche cambiando solo l'email, l'invariante "internal email richiede phone" viene comunque verificato perché lo stato è considerato nella sua interezza.
- **`Delta<Email>.From`**: idempotenza sul singolo campo.
- **Evento `CustomerUpdated` con `["Email"]`**: il consumer riceve lo stesso evento di un update generico, ma con il solo campo cambiato.

## Il seam condiviso: BuildValidatedState

```csharp
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
```

Questo è il **cuore Decide** dell'aggregate. Tutte le operazioni che producono uno stato passano da qui:

- `Create(request)` → `BuildValidatedState(CreateCustomerRequest)` → `BuildValidatedState(CustomerStateInput)`
- `Update(request)` → `BuildValidatedState(UpdateCustomerRequest)` → `BuildValidatedState(CustomerStateInput)`
- `ChangeEmail(email)` → `BuildValidatedState(CustomerStateInput(Name.Value, email, Phone.Value))`
- `ChangePhone(phone)` → `BuildValidatedState(CustomerStateInput(Name.Value, Email.Value, phone))`

Avere **un solo punto di Decide** significa che le regole di validazione restano coerenti tra Create, Update, e target edits. Quando aggiungi una regola, non rischi di dimenticarla in uno dei rami.

`CustomerStateInput` è una `private sealed record` nested. Non esce mai dall'aggregate.

## Gli invarianti cross-field

```csharp
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

Gli invarianti che coinvolgono più di un campo si verificano **sullo stato completo già validato**, non sui delta. Questo evita race condition tra "il vecchio email" e "il nuovo phone" durante un update parziale.

## I tipi nested privati

```csharp
private sealed record CustomerStateInput(string? Name, string? Email, string? Phone);

private sealed record ValidatedState(CustomerName Name, Email Email, PhoneNumber Phone);

private sealed record ChangeSet(
    Delta<CustomerName> Name,
    Delta<Email> Email,
    Delta<PhoneNumber> Phone)
{
    public bool HasChanges => Name.IsChanged || Email.IsChanged || Phone.IsChanged;

    public IReadOnlyCollection<string> ChangedFields { /* ... */ }

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
```

Tre type nested, tutti `private sealed record`:

| Tipo | Ruolo |
|---|---|
| `CustomerStateInput` | Input intermedio per il seam Decide. Solo `string?`. |
| `ValidatedState` | Stato fully-validated. Solo VO. Mai esposto. |
| `ChangeSet` | Differenza tra stato corrente e validato. Contiene `Delta<T>` per ogni campo. |

**Perché privati e concreti** (non `IValidatedState<T>` generico):

- Sono dettagli implementativi di **questo** aggregate.
- Un'astrazione generica creerebbe rumore senza dare flessibilità (vedi ADR-001 sul "no premature generic abstractions").
- Nested → access ai private setter di `Customer` (vedi `ApplyTo`).

## La mappa delle operazioni

```
                        ┌─────────────────────┐
                        │ BuildValidatedState │ ← seam Decide unico
                        └──────────┬──────────┘
                                   │
       ┌───────────────────┬───────┴────────┬──────────────────┐
       ▼                   ▼                ▼                  ▼
   Create(req)        Update(req)      ChangeEmail(e)    ChangePhone(p)
       │                   │                │                  │
       │                   ▼                ▼                  ▼
       │             CheckInvariants  CheckInvariants    CheckInvariants
       │                   │                │                  │
       │                   ▼                ▼                  ▼
       │             ChangeSet.Diff    Delta<Email>      Delta<PhoneNumber>
       │                   │                │                  │
       ▼                   ▼                ▼                  ▼
   new Customer       ApplyTo(this)    this.Email = ...   this.Phone = ...
   Raise Created      Raise Updated    Raise Updated      Raise Updated
                      (se changes)     (se delta)         (se delta)

   Activate() / Deactivate() — non passano per BuildValidatedState
   perché cambiano solo Status (campo singolo, no input esterno)
```

## I test

I test del Customer sono in `tests/DddEntityContracts.Domain.Tests/Customers/`:

| File | Cosa copre |
|---|---|
| `CustomerCreationTests.cs` | Happy path, accumulation errori, `CustomerId.From(Guid.Empty)` guard |
| `CustomerUpdateTests.cs` | Update full, no-op idempotente, ChangedFields, `Update_InvalidField_NoPartialMutation_NoEvents` |
| `CustomerTargetedEditTests.cs` | `ChangeEmail`/`ChangePhone`, delta detection |
| `CustomerBehaviorTests.cs` | `Activate`/`Deactivate`, idempotenza, reason |
| `CustomerValueObjectsTests.cs` | Equality, validazione singoli VO, trim, lowercase |

Vedi [07 — Strategia di test](07-testing.md) per il dettaglio.

## Prossimo passo

Per capire come gli eventi sollevati dall'aggregate vengono intercettati e tradotti: [05 — Application, eventi e outbox](05-application-events-outbox.md).

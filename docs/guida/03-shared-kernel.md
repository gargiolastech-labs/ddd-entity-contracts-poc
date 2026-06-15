# 03 — Shared Kernel

Lo **Shared Kernel** è l'insieme di primitive riusabili che ogni aggregate del Domain può usare. Vivono in `src/DddEntityContracts.Domain/SharedKernel/` e sono indipendenti dall'aggregate specifico.

Le primitive sono volutamente poche e piccole:

| Tipo | Responsabilità | File |
|---|---|---|
| `Error` | Errore di dominio strutturato (codice + messaggio) | `Error.cs` |
| `Result` / `Result<T>` | Esito short-circuit di un'operazione | `Result.cs` |
| `Validation<T>` | Esito accumulativo della costruzione di un VO o stato | `Validation.cs` |
| `Delta<T>` | Cambiamento esplicito di un singolo campo | `Delta.cs` |
| `Entity<TId>` | Base per entità con identità e eventi di dominio | `Entity.cs` |
| `IDomainEvent` | Marker per eventi di dominio | `DomainContracts.cs` |
| `IDomainCreatable<TSelf, TRequest>` | Contratto di creazione via CRTP | `DomainContracts.cs` |
| `IDomainUpdatable<TRequest>` | Contratto di update | `DomainContracts.cs` |
| `ValueObjectGuards` | Helper di validazione (`NotNullOrWhiteSpace`, `MaxLength`, `Matches`) | `ValueObjectGuards.cs` |
| `StronglyTypedId` | Reference shape per gli ID strongly-typed | `StronglyTypedId.cs` |

---

## Error

```csharp
public sealed record Error(string Code, string Message);
```

Una `Error` è un value object immutabile. Il `Code` è machine-readable (es. `Email.InvalidFormat`), il `Message` è human-readable.

**Convenzione per i codici:**

```
<Subject>.<Reason>
```

Esempi: `Email.Required`, `Email.MaxLength`, `Email.InvalidFormat`, `Customer.InternalEmailRequiresPhone`.

Il factory `Error.Create(code, message)` valida via `ArgumentException.ThrowIfNullOrWhiteSpace` che nessuno dei due sia vuoto. Il costruttore del record può essere invocato direttamente quando si è certi degli input.

---

## Result vs Validation

Sono i due tipi più importanti di tutta la libreria. Esistono entrambi perché risolvono **due problemi diversi**.

### Result<T> — monadico, short-circuit

Pensa a `Result<T>` come a una pipeline a step sequenziali: se uno step fallisce, gli step successivi non vengono eseguiti.

```csharp
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public IReadOnlyCollection<Error> Errors { get; }
    public Error? FirstError { get; }

    public static Result Success();
    public static Result Failure(Error error);
    public static Result Failure(IEnumerable<Error> errors);
}

public class Result<T> : Result
{
    public T Value { get; }   // throws se IsFailure

    public Result<TOut> Map<TOut>(Func<T, TOut> mapper);
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> binder);
}
```

**Quando usarlo:** quando il fallimento di uno step rende inutili o impossibili gli step successivi. Esempio: prima validi lo stato, poi controlli gli invarianti cross-field. Se la validazione fallisce, non ha senso controllare gli invarianti.

```csharp
// Esempio reale da Customer.Create
return BuildValidatedState(request)
    .ToResult()                                              // ← bordo Validation → Result
    .Bind(state => CheckCrossFieldInvariants(state).ToResult()) // se fallisce qui, Map non viene chiamato
    .Map(state => { /* costruisce e ritorna il Customer */ });
```

**Invariante di Result:** un `Result` di tipo `Failure` deve contenere almeno un errore. Il costruttore lancia `InvalidOperationException` se viene istanziato con `IsSuccess = false` e zero errori.

### Validation<T> — applicativo, accumula

Pensa a `Validation<T>` come a un set di controlli **paralleli**: tutti vengono eseguiti, e gli errori si sommano. Il chiamante riceve l'elenco completo, non solo il primo.

```csharp
public sealed class Validation<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public static Validation<T> Success(T value);
    public static Validation<T> Failure(Error error);
    public static Validation<T> Failure(IEnumerable<Error> errors);

    public Result<T> ToResult();   // ← ponte verso il mondo monadico

    public static Validation<TResult> Combine<T1, T2, TResult>(
        Validation<T1> first,
        Validation<T2> second,
        Func<T1, T2, TResult> projector);

    public static Validation<TResult> Combine<T1, T2, T3, TResult>(...);
}
```

**Quando usarlo:** quando vuoi che il client riceva *tutti* gli errori di un form, non solo il primo. Esempio: l'utente invia `{ name: "", email: "bad", phone: "abc" }` — il server deve rispondere con tutti e tre gli errori.

```csharp
// Esempio reale da Customer.BuildValidatedState
var vName  = CustomerName.Create(input.Name);   // Validation<CustomerName>
var vEmail = Email.Create(input.Email);         // Validation<Email>
var vPhone = PhoneNumber.Create(input.Phone);   // Validation<PhoneNumber>

return Validation<ValidatedState>.Combine(
    vName, vEmail, vPhone,
    (name, email, phone) => new ValidatedState(name, email, phone));
```

Se anche solo uno dei tre fallisce, `Combine` ritorna `Validation<ValidatedState>.Failure` con la concatenazione di **tutti** gli errori.

**Invariante di Validation<T>:** un `Validation` di tipo `Failure` deve contenere almeno un errore. `Failure(IEnumerable<Error>)` con collezione vuota lancia `InvalidOperationException`.

### Confine tra i due mondi: ToResult()

`Validation<T>.ToResult()` è l'**unico ponte ufficiale**. Si usa quando si esce dalla fase accumulativa per entrare in una catena short-circuit.

```
Mondo accumulativo (Validation<T>)         Mondo short-circuit (Result<T>)
  ┌─────────────────────────────┐             ┌────────────────────────────┐
  │ CustomerName.Create         │             │ Bind → CheckInvariants     │
  │ Email.Create                │  ToResult() │ Map  → Build Customer      │
  │ PhoneNumber.Create          │ ─────────►  │ Map  → Raise CustomerCreated│
  │  Combine accumula errori    │             │                            │
  └─────────────────────────────┘             └────────────────────────────┘
```

**Regola pratica:** dentro un VO o dentro la fase Decide di un aggregate, usa `Validation<T>` e `Combine`. Quando l'aggregate consegna il risultato al chiamante esterno, passa a `Result<T>` con `ToResult()`.

---

## Delta<T>

```csharp
public readonly record struct Delta<T>(T? Value, bool IsChanged)
{
    public static Delta<T> Changed(T? value)   => new(value, true);
    public static Delta<T> Unchanged(T? value) => new(value, false);

    public static Delta<T> From(T? current, T? next)
    {
        bool equal = EqualityComparer<T?>.Default.Equals(current, next);
        return equal ? Unchanged(current) : Changed(next);
    }
}
```

`Delta<T>` esprime in modo esplicito se un valore è **cambiato** oppure no. Si usa nel pattern Diff durante l'Update:

```csharp
var nameDelta  = Delta<CustomerName>.From(current.Name, next.Name);
var emailDelta = Delta<Email>.From(current.Email, next.Email);
var phoneDelta = Delta<PhoneNumber>.From(current.Phone, next.Phone);
```

### Perché esiste

Senza `Delta<T>`, sarebbe naturale scrivere:

```csharp
// ❌ ANTI-PATTERN: null = "non cambiare"
public void Update(string? newEmail)
{
    if (newEmail != null) Email = newEmail;
}
```

Questo conflitta con un requisito legittimo: un campo nullable può voler essere **valorizzato a null** (es. "rimuovi il telefono"). Con la convenzione sopra non lo distingueresti mai da "non passare un nuovo telefono".

`Delta<T>` separa i due concetti: `Value` è il valore (eventualmente null), `IsChanged` è il flag indipendente. Il confronto in `From()` usa `EqualityComparer<T?>.Default`, che è la sorgente di verità per "cambiato/non cambiato" — non un null check.

### Nota sul naming

La proprietà si chiama `IsChanged` (non `Changed`) per non confliggere con il metodo statico `Changed(T?)`. È una decisione esplicita: cambiarla romperebbe la simmetria semantica con `Unchanged(T?)`.

---

## Entity<TId>

```csharp
public abstract class Entity<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(TId id) { Id = id; }

    public TId Id { get; }
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

Base class per qualunque entità con identità. Espone una collezione **read-only** di eventi e un metodo `protected` `Raise` accessibile solo dall'entità stessa o dalle sue sottoclassi.

`ClearDomainEvents` è pubblico perché serve all'infrastructure (un layer di persistence/dispatch tipicamente fa: estrae gli eventi → li dispatcha → li ripulisce per evitare ri-dispatch).

`Raise` lancia se l'evento è null: è un bug di programmazione, non un errore di business.

### Constraint `TId : notnull`

L'ID di un'entità non può essere `null`. Per `record struct` (come `CustomerId`) questo è già garantito staticamente; per `class`-based ID lo è grazie a `notnull`.

---

## IDomainCreatable<TSelf, TRequest> (CRTP)

```csharp
public interface IDomainCreatable<TSelf, in TRequest>
    where TSelf : IDomainCreatable<TSelf, TRequest>
{
    static abstract Result<TSelf> Create(TRequest request);
}
```

Contratto **statico** che impone agli aggregate di esporre un factory `Create(TRequest)` che ritorna `Result<TSelf>`. Usa il pattern **Curiously Recurring Template Pattern (CRTP)** combinato con `static abstract interface members` (C# 11+).

Il vantaggio: chi guarda l'interfaccia sa subito che `Customer.Create(CreateCustomerRequest)` esiste e ritorna `Result<Customer>` senza dover leggere il codice di `Customer`.

```csharp
public sealed class Customer : 
    Entity<CustomerId>,
    IDomainCreatable<Customer, CreateCustomerRequest>,
    IDomainUpdatable<UpdateCustomerRequest>
{
    public static Result<Customer> Create(CreateCustomerRequest request) { /* ... */ }
    public Result Update(UpdateCustomerRequest request) { /* ... */ }
}
```

---

## ValueObjectGuards

Helper statici che ritornano `Validation<T>`, non eccezioni. Vengono usati internamente dai factory dei Value Object.

```csharp
ValueObjectGuards.NotNullOrWhiteSpace(raw, "Email.Required", "Email is required.");
ValueObjectGuards.MaxLength(value, 100, "CustomerName.MaxLength", "Name too long.");
ValueObjectGuards.Matches(value, pattern, "Email.InvalidFormat", "Invalid email.");
```

Tutti i guard ritornano `Validation<string>` (o `Validation<T>` analogo). I VO compongono i guard sequenzialmente (early-exit) o in parallelo (`Combine`) a seconda del bisogno.

Esempio sequenziale (per dipendenze: prima check non-vuoto, poi check lunghezza sul valore non vuoto):

```csharp
var notEmpty = ValueObjectGuards.NotNullOrWhiteSpace(raw, ...);
if (notEmpty.IsFailure)
    return Validation<CustomerName>.Failure(notEmpty.ToResult().Errors);

var value = notEmpty.ToResult().Value.Trim();

var maxLength = ValueObjectGuards.MaxLength(value, 100, ...);
if (maxLength.IsFailure)
    return Validation<CustomerName>.Failure(maxLength.ToResult().Errors);

return Validation<CustomerName>.Success(new CustomerName(value));
```

Esempio parallelo (controlli indipendenti, accumulazione completa):

```csharp
var vMaxLength = ValueObjectGuards.MaxLength(normalized, 254, ...);
var vFormat    = ValueObjectGuards.Matches(normalized, EmailPattern, ...);

return Validation<Email>.Combine(vMaxLength, vFormat, (_, _) => new Email(normalized));
```

La scelta tra sequenziale e parallelo è di design del VO, non casuale.

### VO multi-campo: il caso Money

`Money` (in `src/DddEntityContracts.Domain/Products/ProductValueObjects.cs`) è un esempio di VO con **due campi correlati** validati insieme: `Amount` (decimal) e `Currency` (string ISO 3-letter). Ogni campo ha le sue regole, ma il VO non esiste se l'una o l'altra è invalida.

```csharp
public static Validation<Money> Create(decimal? amount, string? currency)
{
    var vAmount   = ValidateAmount(amount);     // Validation<decimal>
    var vCurrency = ValidateCurrency(currency); // Validation<string>

    return Validation<Money>.Combine(
        vAmount, vCurrency,
        (a, c) => new Money(a, c));
}
```

Punti chiave:

- I due metodi privati (`ValidateAmount`, `ValidateCurrency`) ritornano ognuno `Validation<T>` con il proprio tipo. `Amount` non è una `string`, quindi non si usa `ValueObjectGuards.NotNullOrWhiteSpace` direttamente — la validazione del decimal è inline.
- `Combine` accumula errori: se sia `amount` sia `currency` sono invalidi, il client riceve entrambi gli errori.
- Il costruttore privato di `Money` viene chiamato **solo** quando entrambe le validazioni sono success. Non esiste un `Money` con `Amount` negativo o `Currency` malformata.

Questo è il pattern da usare ogni volta che un VO ha **più di un attributo** che deve essere validato. Vedi `Money.Create` per l'implementazione completa.

---

## StronglyTypedId

```csharp
public readonly record struct StronglyTypedId(Guid Value)
{
    public static StronglyTypedId New() => new(Guid.NewGuid());
    public static StronglyTypedId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
```

Riferimento **da copiare**, non da estendere. `record struct` non è ereditabile, quindi ogni ID di dominio (`CustomerId`, `OrderId`, …) replica questa forma con il proprio nome.

Si vede nella pratica con `CustomerId`:

```csharp
public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.NewGuid());

    public static CustomerId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("CustomerId cannot be empty.", nameof(value));
        return new(value);
    }

    public override string ToString() => Value.ToString();
}
```

Nota la differenza rispetto a `StronglyTypedId.From`: `CustomerId.From` aggiunge il guard contro `Guid.Empty`. L'identità di un'entità non può essere vuota.

---

## Prossimo passo

Per vedere queste primitive in azione su un aggregate completo: [04 — Customer walkthrough](04-customer-walkthrough.md).

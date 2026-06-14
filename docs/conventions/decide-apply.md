# Decide / Apply Pattern

## Obiettivo

Garantire che le operazioni di creazione e aggiornamento di un aggregate rispettino due invarianti fondamentali:

1. **nessun aggregate può trovarsi in uno stato parzialmente valido**: la validazione deve essere atomica;
2. **la mutazione è separata dalla validazione**: non si muta mai durante la fase di controllo degli errori.

Il pattern divide ogni operazione in due fasi sequenziali e non sovrapponibili: **Decide** e **Apply**.

---

## Principio

```
Input raw → [Decide] → ValidatedState (privato) → [Apply] → Aggregate mutato + DomainEvents
```

- **Decide** è pura: non produce effetti collaterali, non muta l'aggregate.
- **Apply** è imperativa: muta l'aggregate e produce eventi, ma non valida.
- Le due fasi non si mescolano mai.

---

## Fase Decide

La fase Decide:

- riceve l'input grezzo (`CreateRequest` o `UpdateRequest`);
- costruisce i Value Object tramite le rispettive `Create(...)` factory;
- usa `Validation<T>` per accumulare tutti gli errori (non cortocircuita presto);
- verifica invarianti cross-field sull'intero stato prospettico validato;
- costruisce un oggetto `ValidatedState` concreto e privato all'aggregate;
- **non muta mai l'aggregate**;
- ritorna `Result<ValidatedState>` verso la fase Apply.

```csharp
// Esempio concettuale — non il tipo finale
public Result<CustomerValidatedState> Decide(CustomerUpdateRequest request)
{
    return BuildValidatedState(request)
        .Bind(CheckCrossFieldInvariants)   // solo se Validation supporta Bind,
        .ToResult();                        // altrimenti si struttura con Combine
}
```

---

## BuildValidatedState

`BuildValidatedState` è il **seam obbligatorio** tra input raw e stato validato.

```csharp
private static Validation<CustomerValidatedState> BuildValidatedState(CustomerUpdateRequest request)
{
    var vEmail     = Email.Create(request.Email);
    var vFirstName = FirstName.Create(request.FirstName);
    var vLastName  = LastName.Create(request.LastName);

    return Validation<CustomerValidatedState>.Combine(
        vEmail, vFirstName, vLastName,
        (email, firstName, lastName) => new CustomerValidatedState(email, firstName, lastName));
}
```

Regole:

- deve essere l'unico punto di costruzione dello stato validato;
- deve essere condiviso tra `Create` e `Update` quando possibile (stessa logica di costruzione);
- deve restare **concreto sull'aggregate**: non diventa `ValidatedState<T>` generico;
- il tipo `CustomerValidatedState` è privato e non appartiene allo `SharedKernel`.

---

## CheckCrossFieldInvariants

`CheckCrossFieldInvariants` è un hook per le invarianti che dipendono dalla combinazione di più campi.

```csharp
private static Validation<CustomerValidatedState> CheckCrossFieldInvariants(
    CustomerValidatedState state)
{
    // Esempio: firstName e lastName non possono essere identici
    if (state.FirstName.Value == state.LastName.Value)
        return Validation<CustomerValidatedState>.Failure(
            new Error("Customer.SameName", "Nome e cognome non possono essere uguali."));

    return Validation<CustomerValidatedState>.Success(state);
}
```

Regole:

- le invarianti cross-field **devono essere valutate sullo stato finale completo**, non solo sui campi cambiati;
- devono accumulare errori tramite `Validation<T>`;
- non devono mai ricevere un `Delta<T>` come input — il delta appartiene alla fase Apply.

---

## Delta

`Delta<T>` è il primitivo condiviso della fase Apply.

```csharp
public readonly record struct Delta<T>(T? Value, bool IsChanged)
```

Rappresenta esplicitamente:
- `Value`: il valore prospettico da applicare;
- `IsChanged`: se quel valore è effettivamente cambiato rispetto allo stato corrente.

Il flag `IsChanged` è **obbligatorio e non opzionale** perché `null` è un valore di business legittimo (es. un campo opzionale azzerato). Usare `null` come segnale di "non cambiato" sarebbe un bug silenzioso.

```csharp
// CORRETTO
var emailDelta = Delta<Email>.From(currentEmail, state.Email);

// SBAGLIATO — null non significa "non cambiato"
Email? emailDelta = state.Email != currentEmail ? state.Email : null;
```

Factory disponibili:

```csharp
Delta<T>.Changed(value)        // IsChanged = true
Delta<T>.Unchanged(value)      // IsChanged = false
Delta<T>.From(current, next)   // confronto automatico via EqualityComparer
```

---

## Fase Apply

La fase Apply:

- può essere eseguita **solo dopo una Decide valida** (riceve `ValidatedState`, non input raw);
- calcola i delta concreti confrontando lo stato corrente con quello validato;
- muta solo i campi con `delta.IsChanged == true`;
- emette domain event **solo** per i delta effettivamente cambiati;
- non esegue alcuna validazione di business.

```csharp
// Esempio concettuale
private void Apply(CustomerValidatedState state)
{
    var emailDelta = Delta<Email>.From(_email, state.Email);
    var nameDelta  = Delta<PersonName>.From(_name, state.Name);

    if (emailDelta.IsChanged)
    {
        _email = emailDelta.Value!;
        Raise(new CustomerEmailChanged(Id, emailDelta.Value!));
    }

    if (nameDelta.IsChanged)
    {
        _name = nameDelta.Value!;
        Raise(new CustomerNameChanged(Id, nameDelta.Value!));
    }
}
```

---

## Regole obbligatorie

| Regola | Motivazione |
|---|---|
| `BuildValidatedState` è unico punto di accesso allo stato validato | Previene bypass della validazione |
| Decide non muta l'aggregate | Garantisce separazione tra validazione e side effect |
| Apply non valida input raw | Responsabilità singola: mutazione e eventi |
| Invarianti cross-field valutate sullo stato completo, non sul delta | Il delta è un artefatto di Apply, non una view della validità |
| `Delta<T>` usato sempre per ogni campo in Apply | Previene mutazioni silenziose e eventi spuri |
| `IsChanged = true` + `Value = null` è valido e deve essere gestito | `null` è un valore di business legittimo |
| Nessun `ValidatedState<T>` generico | Prematuro: ogni aggregate ha la sua struttura |
| Nessun `ChangeSet<T>` generico | Non estrarre finché non emerge duplicazione reale |

---

## Esempio concettuale

```csharp
// Aggregate (non implementato in questa STORY)
public sealed class Customer : Entity<CustomerId>,
    IDomainCreatable<Customer, CustomerCreateRequest>
{
    private Email _email = default!;
    private PersonName _name = default!;

    private Customer() { }

    // --- Decide + Apply per Create ---

    public static Result<Customer> Create(CustomerCreateRequest request)
    {
        return BuildValidatedState(request)
            .ToResult()
            .Map(state =>
            {
                var customer = new Customer();
                customer.Apply(state);
                return customer;
            });
    }

    // --- Decide + Apply per Update ---

    public Result Update(CustomerUpdateRequest request)
    {
        return BuildValidatedState(request)
            .ToResult()
            .Map(state => { Apply(state); return Result.Success(); })
            .Bind(r => r);
    }

    // --- Seam condiviso ---

    private static Validation<CustomerValidatedState> BuildValidatedState(
        CustomerCreateRequest request)
    {
        var vEmail = Email.Create(request.Email);
        var vName  = PersonName.Create(request.FirstName, request.LastName);
        return Validation<CustomerValidatedState>.Combine(
            vEmail, vName, (e, n) => new CustomerValidatedState(e, n));
    }

    // --- Fase Apply ---

    private void Apply(CustomerValidatedState state)
    {
        var emailDelta = Delta<Email>.From(_email, state.Email);
        if (emailDelta.IsChanged) { _email = emailDelta.Value!; Raise(new CustomerEmailChanged(Id)); }

        var nameDelta = Delta<PersonName>.From(_name, state.Name);
        if (nameDelta.IsChanged) { _name = nameDelta.Value!; Raise(new CustomerNameChanged(Id)); }
    }

    private sealed record CustomerValidatedState(Email Email, PersonName Name);
}
```

---

## Cosa non fare

| Anti-pattern | Problema |
|---|---|
| Introdurre `ValidatedState<T>` generico | Astrazione prematura: ogni aggregate ha il suo stato |
| Introdurre `ChangeSet<T>` generico | Stesso problema, non estrarre prima che emerga duplicazione reale |
| Usare `null` per indicare "non cambiato" | `null` è un valore legittimo: usa `Delta<T>.IsChanged` |
| Mutare l'aggregate durante Decide | Viola la separazione tra validazione e side effect |
| Validare invarianti cross-field solo sui delta | Il delta non rappresenta l'entità completa |
| Emettere eventi se il valore non è cambiato | Events spuri inquinano l'event log e l'infrastruttura |
| Accettare input raw nella fase Apply | Apply non sa validare: riceve solo `ValidatedState` |

---

## Nota per Analyzer futuro

Questa convenzione sarà la base per le regole di analisi statica pianificate:

| Regola | Descrizione |
|---|---|
| `DA001` | Aggregate senza metodo `BuildValidatedState` |
| `DA002` | Mutazione dell'aggregate rilevata nella fase Decide |
| `DA003` | Assegnazione diretta da request raw senza passare per VO `Create` |
| `DA004` | Campo mutato in Apply senza controllo `Delta<T>.IsChanged` |
| `DA005` | Domain event emesso senza `Delta<T>.IsChanged = true` |
| `DA006` | Invarianti cross-field verificate su singoli delta invece che sullo stato completo |

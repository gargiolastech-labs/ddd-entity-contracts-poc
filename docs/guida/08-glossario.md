# 08 — Glossario

Glossario dei termini DDD e architetturali usati nel codebase. Per ogni termine, definizione + dove vederlo in pratica nel progetto.

---

## Aggregate

Cluster di oggetti del dominio trattati come una **singola unità transazionale**. Ha una **root entity** (chiamata Aggregate Root) che è l'unica via di accesso esterna.

Nel PoC: `Customer` è un Aggregate Root semplice. Il cluster è composto da `Customer` + i suoi VO (`CustomerName`, `Email`, `PhoneNumber`).

Regola: **operazioni di scrittura esterne** parlano sempre con l'Aggregate Root, mai con i tipi interni. Sono i metodi pubblici di `Customer` (`Create`, `Update`, `Activate`, `Deactivate`, `ChangeEmail`, `ChangePhone`).

Vedi: `src/DddEntityContracts.Domain/Customers/Customer.cs`.

---

## Aggregate Root

L'entità che fa da "punto di ingresso" all'aggregate. Espone le operazioni di business; tutto il resto è dietro un setter privato.

Nel PoC: `Customer` (eredita da `Entity<CustomerId>`).

---

## Applicative (Validation)

Stile di composizione dove le operazioni vengono eseguite **in parallelo** e gli errori vengono **accumulati**. Opposto di monadico.

Nel PoC: `Validation<T>.Combine(...)` è applicativo. Tutti i `Validation<T>` passati vengono valutati; se tutti success, viene chiamato il projector con i valori; se almeno uno è failure, la composizione produce un failure con la concatenazione di tutti gli errori.

---

## At-least-once delivery

Garanzia di consegna in cui un messaggio può essere consegnato **una o più volte**, ma mai zero. Implica che il consumer deve essere **idempotente**.

Nel PoC: l'Outbox lavora in at-least-once. Per questo gli `OutboxMessage` portano una `DeduplicationKey`.

---

## Behavioral operation

Operazione su un aggregate che ha **un nome di business** (non "update di un campo"). Esempi: `Activate`, `Deactivate`, `Approve`, `Reject`, `Cancel`, `Ship`.

Una behavioral operation solleva tipicamente un evento dedicato (`CustomerActivated`, non solo `CustomerUpdated`).

Vedi: `Customer.Activate`, `Customer.Deactivate`, ed eventi `CustomerActivated`, `CustomerDeactivated`.

---

## Bounded Context

Confine semantico esplicito dentro cui un termine di dominio ha **un solo significato**. "Customer" nel contesto Sales può essere diverso da "Customer" nel contesto Billing.

Nel PoC: c'è un solo bounded context implicito (il dominio Customer). I namespace `Domain.Customers` e `Application.Customers.EventHandlers` marcano questo confine.

---

## CRTP (Curiously Recurring Template Pattern)

Pattern dove un tipo dichiara di implementare un'interfaccia parametrizzata su se stesso, tipicamente per esporre **metodi statici** noti staticamente al chiamante.

Nel PoC: `IDomainCreatable<TSelf, TRequest> where TSelf : IDomainCreatable<TSelf, TRequest>`. Implementato da `Customer : IDomainCreatable<Customer, CreateCustomerRequest>`.

Permette: `Customer.Create(request)` è verificato a compile time grazie a `static abstract Result<TSelf> Create(TRequest request)`.

---

## Decide / Apply (pattern)

Pattern fondamentale del PoC. Ogni operazione che muta un aggregate è divisa in due fasi:

1. **Decide** — pura, validazione, costruisce uno `ValidatedState`.
2. **Apply** — imperativa, muta lo stato e solleva eventi, dopo che Decide ha avuto successo.

Le due fasi non si mescolano mai. Approfondimento: [conventions/decide-apply.md](../conventions/decide-apply.md).

---

## Delta<T>

Value object del Shared Kernel che esprime in modo esplicito "il valore X **è cambiato** rispetto al precedente". Distinto da "null = non cambiare".

Nel PoC: `src/DddEntityContracts.Domain/SharedKernel/Delta.cs`. Usato in `Customer.ChangeSet` per il diff pre-apply.

---

## Domain Event

Fatto immutabile **avvenuto nel passato** all'interno del dominio. Si chiama al passato: `CustomerCreated`, `CustomerActivated`, non `CreateCustomer`.

È sollevato dall'aggregate quando lo stato cambia in modo significativo. Vive solo nel Domain.

Nel PoC: `IDomainEvent` marker in `DomainContracts.cs`. Implementazioni in `CustomerEventsAndRequests.cs`.

---

## DTO (Data Transfer Object)

Oggetto piatto che porta dati attraverso un confine. Non ha comportamento.

Nel PoC: gli integration event sono DTO (`CustomerRegisteredIntegrationEvent`, ecc.). Le request sono DTO (`CreateCustomerRequest`, `UpdateCustomerRequest`).

---

## Entity

Oggetto del dominio definito dalla sua **identità**, non dai suoi attributi. Due entità con stessi attributi ma ID diverso sono entità diverse.

Nel PoC: `Customer` è un'entità. `Entity<TId>` è la base class.

Contrapposto a: **Value Object**.

---

## Idempotenza

Proprietà di un'operazione tale che eseguirla N volte produce lo stesso effetto di eseguirla 1 volta.

Nel PoC:

- `Customer.Activate()` su un customer già attivo: nessun evento, ritorna `Success`.
- `Customer.Deactivate()` su un customer già inattivo: idem.
- `Customer.Update(request)` con request identico allo stato corrente: nessun evento.
- Gli integration events portano `DeduplicationKey` perché la consegna at-least-once richiede idempotenza downstream.

---

## Integration Event

Fatto pubblicato fuori dal bounded context, destinato a consumatori esterni. È un DTO stabile, versionabile.

Nel PoC: `CustomerRegisteredIntegrationEvent`, `CustomerDeactivatedIntegrationEvent` in `Application.Contracts.IntegrationEvents`. Mai contengono tipi di dominio.

Distinto da: **Domain Event** (interno, ricco).

---

## Invariant (invariante)

Proprietà che **deve sempre essere vera** in un certo contesto. L'aggregate ha la responsabilità di farla rispettare.

Nel PoC: nel Customer, l'invariante "se l'email è `@internal.local`, allora il phone deve essere presente" è verificato in `CheckCrossFieldInvariants`.

Altri esempi: "un `Result` failure deve avere almeno un errore" (verificato in `Result.cs`); "un `CustomerId` non può essere `Guid.Empty`" (verificato in `CustomerId.From`).

---

## Monadic (Result)

Stile di composizione dove le operazioni vengono eseguite **in sequenza** e il primo errore **interrompe** la catena (short-circuit).

Nel PoC: `Result<T>.Bind(...)` è monadico. Se la pipeline `BuildValidatedState().ToResult().Bind(CheckInvariants).Map(BuildCustomer)` fallisce in `Bind`, `Map` non viene mai chiamato.

Opposto a: **applicative** (vedi `Validation<T>`).

---

## Outbox (pattern)

Pattern transazionale per garantire consistency tra cambio di stato del dominio e pubblicazione di messaggi verso l'esterno.

Si scrive il messaggio in una tabella `outbox` **nella stessa transazione** del cambio di stato. Un worker separato legge la tabella e pubblica sul broker (HTTP, queue, ecc.).

Nel PoC: astratto da `IOutboxWriter`. Implementazione PoC: `InMemoryOutboxWriter` (stub). Produzione: store DB-backed transazionale.

---

## ProblemDetails (RFC 7807)

Formato standard per gli error payload HTTP. Specifica `type`, `title`, `status`, `detail` come campi obbligatori, ed extension custom (qui: `errors`, `errorCodes`).

Nel PoC: `ResultToProblemDetailsBehavior` traduce `Result` falliti in ProblemDetails.

---

## record vs record struct vs readonly record struct

| Costrutto | Allocazione | Equality | Caso d'uso PoC |
|---|---|---|---|
| `class` | Heap | Reference | Aggregate (`Customer`) |
| `record` (record class) | Heap | Value | Eventi, request, DTO |
| `sealed record` | Heap | Value | VO con costruzione vincolata (`CustomerName`, `Email`, `PhoneNumber`) |
| `record struct` | Stack | Value | (non usato qui per design choice) |
| `readonly record struct` | Stack | Value | Strongly-typed ID (`CustomerId`), `Delta<T>` |

Tutti i record hanno value equality automatica + `ToString` + deconstruct.

---

## Result vs Validation

I due tipi più importanti del Shared Kernel.

- **Result** è monadico (short-circuit). Per step sequenziali.
- **Validation** è applicativo (accumula). Per check paralleli.

Confine esplicito: `Validation<T>.ToResult()`.

Approfondimento: [03 — Shared Kernel](03-shared-kernel.md#result-vs-validation).

---

## Shared Kernel

Sottoinsieme del Domain che contiene primitive **condivise** da tutti gli aggregate. Modificarlo impatta tutti — quindi cambia raramente.

Nel PoC: `src/DddEntityContracts.Domain/SharedKernel/`. Contiene `Result`, `Validation`, `Delta`, `Entity`, `Error`, `ValueObjectGuards`, `StronglyTypedId`, contratti.

---

## Side-effect

Effetto osservabile prodotto da un'operazione, **al di là del valore di ritorno**. Esempi: invio email, scrittura su disco, chiamata HTTP, modifica di una variabile globale.

Nel PoC: `CustomerCreatedWelcomeEmailHandler` produce un side-effect (welcome email). Distinto da `CustomerCreatedToIntegrationEventHandler` che produce un integration event (non un side-effect, un fatto pubblicato).

---

## SUT (System Under Test)

Convenzione di naming nei test: la variabile che rappresenta l'oggetto sotto test.

Nel PoC: usata negli Application.Tests per distinguere l'handler dalle sue fake dependencies.

---

## Validation<T>

Vedi: [Result vs Validation](#result-vs-validation), [applicative](#applicative-validation).

---

## Value Object

Oggetto del dominio definito **interamente dal suo valore**. Due VO con stessi attributi sono **uguali**. Immutabili, validati alla nascita.

Nel PoC: `CustomerName`, `Email`, `PhoneNumber` sono VO. Anche `CustomerId` (record struct con value semantics).

Approfondimento: [conventions/value-object-authoring.md](../conventions/value-object-authoring.md).

Contrapposto a: **Entity**.

---

## Prossimo passo

Per la procedura operativa quando si introduce un nuovo aggregate nel dominio: [09 — Aggiungere una nuova entità](09-aggiungere-nuova-entita.md).

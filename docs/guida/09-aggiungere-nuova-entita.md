# 09 — Aggiungere una nuova entità: guida step-by-step

Questa guida descrive **in che ordine** procedere quando si introduce un nuovo aggregate nel dominio. È complementare al [Aggregate Authoring Template](../templates/aggregate-authoring-template.md), che fornisce lo scheletro di codice da copiare: qui spieghiamo la **sequenza operativa**, i checkpoint di verifica, e le decisioni da prendere prima di iniziare a scrivere codice.

Useremo come esempio l'introduzione fittizia di un aggregate `Order`, per concretizzare i passaggi senza dover inventare un dominio inesistente.

---

## Step 0 — Decisioni di design (prima di scrivere codice)

Prima di toccare qualunque file, rispondi a queste domande. Se anche una sola risposta è incerta, fermati e discuti col team.

### 0.1 È davvero un nuovo aggregate?

Un aggregate ha:

- **identità persistente** (esiste nel tempo, ha un ID univoco);
- **invarianti propri** che deve far rispettare;
- **ciclo di vita** (creazione, modifiche, eventuali transizioni di stato);
- **un boundary transazionale**: tutte le sue modifiche sono atomiche.

Se quello che stai modellando è solo un valore (es. `MonetaryAmount`, `EmailAddress`, `Coordinate`), è un **Value Object**, non un aggregate. Vedi [conventions/value-object-authoring.md](../conventions/value-object-authoring.md).

Se è una proiezione read-only, è un **read model**, non un aggregate.

Se rappresenta un workflow che attraversa più aggregate, è una **saga / process manager**, non un aggregate.

### 0.2 Quali sono gli attributi modificabili?

Elenca i campi che il client può valorizzare o cambiare. Per ognuno:

- È **obbligatorio** o **opzionale** (nullable)?
- È un valore primitivo (`string`, `int`, ...) o un VO con regole proprie (es. `Email`)?
- Quali sono i vincoli di validazione (lunghezza, regex, range)?

Esempio Order: `CustomerId`, `ShippingAddress`, `Items[]`, `Notes (opzionale)`.

### 0.3 Quali sono gli invarianti cross-field?

Sono le regole che coinvolgono più di un campo o l'intero stato dell'aggregate.

Esempio Order: "se la spedizione è internazionale, le note devono includere la dichiarazione doganale".

Questi vivranno in `CheckCrossFieldInvariants`.

### 0.4 Quale ciclo di vita ha?

Anche solo "esiste/non esiste" è un ciclo di vita.

Esempi più ricchi:

- Order: `Draft → Confirmed → Shipped → Delivered`.
- Customer: `Active ↔ Inactive`.
- Subscription: `Trial → Active → Suspended → Cancelled`.

Ogni transizione **significativa** sarà una behavioral operation con un evento dedicato (`OrderConfirmed`, `OrderShipped`, ...).

### 0.5 Quali eventi di dominio solleva?

Mappa una-a-una le operazioni significative ai loro eventi.

Pattern di default:

| Operazione | Evento |
|---|---|
| Creazione | `<Aggregate>Created` |
| Update generico di attributi | `<Aggregate>Updated(ChangedFields)` |
| Transizione di stato A→B | `<Aggregate>StateB` (es. `OrderShipped`) |

**Non** introdurre eventi per-campo (`OrderShippingAddressChanged`): usa `OrderUpdated` con `ChangedFields`. Eventi semantici solo per transizioni di **stato**, non di attributi.

### 0.6 Servono integration events?

Domande:

- C'è un sistema esterno che deve sapere che questo evento è successo?
- C'è un side-effect interno (email, notifica push, audit log) che deve scattare?

Se nessuna delle due, non serve nulla in Application. Lo aggiungerai dopo, quando un consumer reale lo richiederà.

Se sì:

- Side-effect interno → handler in Application.
- Evento per il mondo esterno → integration event DTO + handler che lo accoda in Outbox.

Vedi [05 — Application, eventi e outbox](05-application-events-outbox.md) per il pattern.

### 0.7 Va esposto via HTTP?

Se sì, in che forma?

- `POST /api/orders` (create) → `Result<Order>` → response 201 o 400 ProblemDetails.
- `PATCH /api/orders/{id}` (update) → `Result` → 200 o 400.
- `POST /api/orders/{id}/confirm` (behavioral) → `Result` → 200 o 400.

Vedi [06 — API e ProblemDetails](06-api-problemdetails.md) per il pattern endpoint filter.

---

## Step 1 — Crea i Value Object e l'ID

**Cosa fai:** definisci tutti i tipi che rappresentano valori (immutabili, validati alla nascita).

**Dove:** un nuovo file `src/DddEntityContracts.Domain/Orders/OrderValueObjects.cs`.

**Cosa crei:**

1. `OrderId` — `readonly record struct` con guard su `Guid.Empty` in `From`. Modello: `CustomerId` in `CustomerValueObjects.cs`.
2. Un VO per ciascun attributo "non primitivo" (es. `ShippingAddress`, `OrderNotes`).
3. Eventuali enum per lo stato (`OrderStatus { Draft = 1, Confirmed = 2, ... }`).

**Pattern per ciascun VO** (vedi [03 — Shared Kernel](03-shared-kernel.md#valueobjectguards)):

- `sealed record` con `Value` getter e costruttore `private`.
- `static Validation<TVO> Create(string? raw)` come unico ingresso.
- Validazione sequenziale (early-exit) se gli step sono dipendenti, parallela (`Combine`) se indipendenti.

**Verifica:**

```bash
dotnet build --configuration Release
```

Deve compilare con zero warning. Niente test ancora — i test li scrivi nello step 5.

**Pitfall ricorrenti:**

- Mettere setter pubblici sui VO ("solo per i test"). I VO sono immutabili, fine.
- Lanciare eccezioni nei guard. Usa `ValueObjectGuards`, che ritorna `Validation<T>`.
- Dimenticare il guard `Guid.Empty` su `<Aggregate>Id.From`. Un'identità non può essere vuota.

---

## Step 2 — Crea le request DTO

**Cosa fai:** definisci le forme di input grezze che gli endpoint o gli use case ricevono dall'esterno.

**Dove:** un nuovo file `src/DddEntityContracts.Domain/Orders/OrderEventsAndRequests.cs` (le request e gli eventi tipicamente convivono nello stesso file finché sono pochi).

**Cosa crei:**

```csharp
public sealed record CreateOrderRequest(
    Guid? CustomerId,
    string? ShippingAddress,
    string? Notes);

public sealed record UpdateOrderRequest(
    string? ShippingAddress,
    string? Notes);
```

**Regole:**

- Tutti i campi sono **primitivi** e **nullable** (`string?`, `Guid?`, ...). Il parsing/validazione è responsabilità del dominio.
- `CreateRequest` include i campi necessari a inizializzare l'aggregate. **Non** include lo status o flag di lifecycle.
- `UpdateRequest` include gli stessi campi editabili di `CreateRequest`, **escludendo** quelli immutabili dopo la creazione (es. `CustomerId` non si cambia in un Order esistente).

**Pitfall ricorrenti:**

- Mettere `OrderStatus` nella `UpdateRequest`. Lo stato è behavioral, non si aggiorna come un attributo.
- Mettere VO già costruiti nella request (`ShippingAddress` come VO invece che `string?`). La request è input grezzo; il dominio costruisce i VO.

---

## Step 3 — Crea i domain event

**Cosa fai:** definisci gli eventi che l'aggregate solleverà.

**Dove:** stesso file `OrderEventsAndRequests.cs`.

**Cosa crei:**

```csharp
public sealed record OrderCreated(OrderId OrderId) : IDomainEvent;

public sealed record OrderUpdated(
    OrderId OrderId,
    IReadOnlyCollection<string> ChangedFields) : IDomainEvent;

public sealed record OrderConfirmed(OrderId OrderId) : IDomainEvent;
public sealed record OrderShipped(OrderId OrderId, string TrackingNumber) : IDomainEvent;
public sealed record OrderCancelled(OrderId OrderId, string? Reason) : IDomainEvent;
```

**Regole:**

- Naming al **passato** (è qualcosa che è già successo).
- Tutti implementano `IDomainEvent`.
- Tutti i campi sono di tipo di **dominio** (`OrderId`, non `Guid`). Gli eventi vivono nel Domain e parlano la lingua del Domain.
- Includi nel payload l'informazione **specifica dell'evento**: `OrderShipped(TrackingNumber)`, `OrderCancelled(Reason)`. Non passare l'intero aggregate.

**Verifica:**

```bash
dotnet build --configuration Release
```

---

## Step 4 — Crea l'aggregate

**Cosa fai:** scrivi la classe `Order` seguendo il template, adattandolo agli attributi specifici.

**Dove:** un nuovo file `src/DddEntityContracts.Domain/Orders/Order.cs`.

**Cosa fai operativamente:**

1. **Apri** il [template](../templates/aggregate-authoring-template.md).
2. **Copia** lo scheletro di `AggregateName` in `Order.cs`.
3. **Sostituisci** ogni `AggregateName`, `AggregateId`, `FieldType`, `FieldName` con i tuoi nomi specifici.
4. **Adatta** `BuildValidatedState` al numero esatto di campi (usa l'overload `Combine` a 2, 3 o n argomenti).
5. **Implementa** `CheckCrossFieldInvariants` con i tuoi invarianti.
6. **Aggiungi/rimuovi** behavioral operations e targeted edits in base al tuo dominio.

**Punti di attenzione (vedi il walkthrough del Customer in [04](04-customer-walkthrough.md) per gli esempi reali):**

- **Costruttore privato**, solo `Create` può istanziare.
- **Setter privati** su tutti i campi pubblici.
- **`Apply(state)`** è privato e non valida nulla — si fida.
- **`BuildValidatedState` è il seam unico**. Anche le targeted edit (`ChangeShippingAddress`) passano da qui, costruendo l'input completo.
- **Le nested record (`ValidatedState`, `ChangeSet`, `OrderStateInput`) sono `private sealed`** — non genericizzarle.

**Verifica:**

```bash
dotnet build --configuration Release
```

Zero errori, zero warning. Se warning compaiono, **risolvili prima di proseguire**: con `TreatWarningsAsErrors: true` un warning oggi è un errore domani.

**Pitfall ricorrenti:**

- Validare solo i campi cambiati nell'`Update`. Sbagliato: gli invarianti cross-field potrebbero rompersi sulla parte non cambiata. Validare sempre lo stato prospettico completo.
- Sollevare un evento prima della mutazione. Sbagliato: gli handler leggerebbero stato stantio. Muta prima, raise dopo.
- Sollevare `OrderUpdated` da `Confirm()` o `Ship()`. Sbagliato: sono behavioral, hanno il loro evento semantico.
- Confondere `null` con "non cambiare" in un targeted edit. Usa sempre `Delta<T>`.

---

## Step 5 — Test del Domain

**Cosa fai:** scrivi i test che dimostrano che l'aggregate fa quello che dichiari.

**Dove:** una nuova cartella `tests/DddEntityContracts.Domain.Tests/Orders/`. Replica la struttura di `Customers/`:

```
tests/DddEntityContracts.Domain.Tests/Orders/
  OrderCreationTests.cs
  OrderUpdateTests.cs
  OrderBehaviorTests.cs           (Confirm, Ship, Cancel, ecc.)
  OrderTargetedEditTests.cs       (ChangeShippingAddress, ...)
  OrderValueObjectsTests.cs
```

**Cosa testare in ciascun file:** consulta la **Tests checklist** del [template](../templates/aggregate-authoring-template.md#tests-checklist) e la [strategia di test](07-testing.md).

**Test minimi obbligatori:**

| Test | File | Cosa dimostra |
|---|---|---|
| `Create_WithValidRequest_Succeeds` | `OrderCreationTests` | Happy path |
| `Create_WithMultipleInvalidFields_AccumulatesErrors` | idem | `Validation.Combine` funziona |
| `Create_WithValidRequest_RaisesOrderCreatedEvent` | idem | Evento sollevato |
| `OrderId_From_EmptyGuid_Throws` | idem | Guard ID |
| `Update_InvalidField_NoPartialMutation_NoEvents` | `OrderUpdateTests` | Atomicità Decide/Apply |
| `Update_NoChanges_IsIdempotent_NoEvent` | idem | Idempotenza Update |
| `Update_SingleField_RaisesUpdatedWithCorrectChangedFields` | idem | `ChangedFields` |
| `Confirm_FromDraft_RaisesOrderConfirmedEvent` | `OrderBehaviorTests` | Transizione behavioral |
| `Confirm_AlreadyConfirmed_NoEvent` | idem | Idempotenza behavioral |

**Verifica:**

```bash
dotnet build --configuration Release
dotnet test --configuration Release --no-build
```

Tutti verdi. Se uno fallisce e il comportamento del dominio è quello voluto, il test è sbagliato. Se il comportamento è scorretto, il dominio è sbagliato. Non commentare mai un test.

---

## Step 6 (opzionale) — Application: side-effect e integration events

Salta questo step se l'aggregate non ha né side-effect né integration events.

**Cosa fai:** crei gli handler che intercettano i domain event dell'aggregate.

**Dove:** in `src/DddEntityContracts.Application/`:

```
src/DddEntityContracts.Application/
  Contracts/IntegrationEvents/
    OrderPlacedIntegrationEvent.cs        (nuovo)
    OrderShippedIntegrationEvent.cs       (nuovo)
  Orders/EventHandlers/                    (nuova cartella)
    OrderCreatedToIntegrationEventHandler.cs
    OrderShippedToIntegrationEventHandler.cs
    OrderCreatedNotificationHandler.cs     (se serve side-effect)
```

**Per ciascun integration event:**

1. Definisci il DTO che eredita da `IntegrationEvent`.
2. **Mai includere tipi di dominio** nel DTO (no `OrderId`, solo `Guid`).
3. `Version = 1` di default.
4. Il naming dell'integration event riflette il **punto di vista esterno** (es. `OrderPlaced` invece di `OrderCreated`).

**Per ciascun handler:**

1. Implementa `IDomainEventHandler<TDomainEvent>`.
2. Mappa il domain event in integration event (scomponendo i VO).
3. Serializza con `JsonSerializer.Serialize`.
4. Accoda via `IOutboxWriter.EnqueueAsync` con `Type` stabile (`"OrderPlaced.v1"`) e `DeduplicationKey` significativa.

**Registra gli handler in `ApplicationServiceCollectionExtensions`:**

```csharp
public static IServiceCollection AddApplicationEventHandlers(this IServiceCollection services)
{
    // ... handler esistenti ...
    services.AddScoped<IDomainEventHandler<OrderCreated>, OrderCreatedToIntegrationEventHandler>();
    services.AddScoped<IDomainEventHandler<OrderShipped>, OrderShippedToIntegrationEventHandler>();
    return services;
}
```

**Verifica:**

```bash
dotnet build --configuration Release
```

**Pitfall ricorrenti:**

- Includere `OrderId` (il VO) nel DTO dell'integration event. Usa `Guid`.
- Includere `Email` o `ShippingAddress` (VO) nel DTO. Usa `string`.
- Riusare lo stesso integration event per scopi diversi nel tempo. Quando lo schema cambia in modo breaking, esce `OrderPlacedIntegrationEventV2`.
- Far parlare il Domain di outbox o di integration event. Il Domain non deve sapere nulla di questo livello.

---

## Step 7 (opzionale) — Test Application

Salta se hai saltato lo Step 6.

**Cosa fai:** test per gli handler appena creati.

**Dove:** `tests/DddEntityContracts.Application.Tests/Orders/EventHandlers/`.

**Cosa testare** (modello: `CustomerCreatedToIntegrationEventHandlerTests`):

| Test | Cosa dimostra |
|---|---|
| `OrderCreated_MappedTo_OrderPlacedIntegrationEvent_EnqueuedToOutbox` | Mapping corretto, Type stabile, payload con primitive |
| `OrderCreated_IntegrationEventHandler_IsIdempotent_OnRedelivery` | DeduplicationKey stabile su redelivery |
| `OrderCreated_TriggersNotification` (se hai un side-effect handler) | Side-effect chiamato |

**Riusa i fake** `FakeOutboxWriter` e `FakeCustomerNotificationSender` esistenti se il loro contratto si applica al tuo handler. Crea nuovi fake hand-written se servono nuove abstractions.

**Verifica:**

```bash
dotnet test --configuration Release --no-build
```

---

## Step 8 (opzionale) — Esponi su API

Salta se l'aggregate non è esposto via HTTP.

**Cosa fai:** aggiungi gli endpoint Minimal API.

**Dove:** `src/DddEntityContracts.Api/Program.cs`.

**Pattern di base:**

```csharp
api.MapPost("/orders", (CreateOrderRequest request) =>
{
    var result = Order.Create(request);
    return result.IsSuccess
        ? Results.Created($"/api/orders/{result.Value.Id}", new { id = result.Value.Id })
        : (object)result;   // il filter trasformerà in ProblemDetails
});

api.MapPatch("/orders/{id:guid}", (Guid id, UpdateOrderRequest request, IOrderRepository repo) =>
{
    var order = repo.GetById(OrderId.From(id));
    var result = order.Update(request);
    return result.IsSuccess ? Results.NoContent() : (object)result;
});

api.MapPost("/orders/{id:guid}/confirm", (Guid id, IOrderRepository repo) =>
{
    var order = repo.GetById(OrderId.From(id));
    var result = order.Confirm();
    return result.IsSuccess ? Results.NoContent() : (object)result;
});
```

(Nota: `IOrderRepository` qui è uno stub teorico — il PoC non ha persistenza, quindi questo step va completato solo se hai introdotto un meccanismo di repository nel frattempo.)

**Aggiungi test di integrazione** in `tests/DddEntityContracts.Api.Tests/`. Modello: `ResultToProblemDetailsBehaviorTests`. Usa `WebApplicationFactory<Program>`.

**Verifica:**

```bash
dotnet test --configuration Release --no-build
```

---

## Step 9 — Build, test, commit, push

**Cosa fai:** chiudi il ciclo, garantendo che la solution sia in stato verde.

```bash
dotnet build --configuration Release
dotnet test  --configuration Release --no-build
```

Devono essere zero errori, zero warning, e tutti i test verdi.

**Commit message convenzionale** per un nuovo aggregate:

```
feat(domain): add Order aggregate

- OrderId, ShippingAddress, OrderNotes value objects
- Order aggregate with Create/Update/Confirm/Ship/Cancel
- Domain events: OrderCreated, OrderUpdated, OrderConfirmed,
  OrderShipped, OrderCancelled
- Integration events: OrderPlacedIntegrationEvent,
  OrderShippedIntegrationEvent
- 23 tests, all green
```

Se non hai aggiunto Application o API, accorcia il summary di conseguenza.

```bash
git add src/ tests/
git commit -m "..."
git push origin <branch>
```

---

## Mappa finale dei file

Riassunto di cosa avrai creato per l'aggregate `Order` completo (Domain + Application + Api):

```
src/DddEntityContracts.Domain/Orders/
  Order.cs                                           ← Step 4
  OrderEventsAndRequests.cs                          ← Step 2 + 3
  OrderValueObjects.cs                               ← Step 1

src/DddEntityContracts.Application/
  Contracts/IntegrationEvents/
    OrderPlacedIntegrationEvent.cs                   ← Step 6
    OrderShippedIntegrationEvent.cs                  ← Step 6
  Orders/EventHandlers/
    OrderCreatedToIntegrationEventHandler.cs         ← Step 6
    OrderShippedToIntegrationEventHandler.cs         ← Step 6
  ApplicationServiceCollectionExtensions.cs          ← Step 6 (modifica)

src/DddEntityContracts.Api/
  Program.cs                                         ← Step 8 (modifica)

tests/DddEntityContracts.Domain.Tests/Orders/
  OrderCreationTests.cs                              ← Step 5
  OrderUpdateTests.cs                                ← Step 5
  OrderBehaviorTests.cs                              ← Step 5
  OrderTargetedEditTests.cs                          ← Step 5
  OrderValueObjectsTests.cs                          ← Step 5

tests/DddEntityContracts.Application.Tests/Orders/EventHandlers/
  OrderCreatedToIntegrationEventHandlerTests.cs      ← Step 7
  OrderShippedToIntegrationEventHandlerTests.cs      ← Step 7

tests/DddEntityContracts.Api.Tests/
  OrderEndpointsTests.cs                             ← Step 8
```

Nessuna modifica strutturale al Shared Kernel. Se hai sentito l'esigenza di aggiungere qualcosa allo Shared Kernel (`Domain/SharedKernel/`), fermati: probabilmente stai introducendo un'astrazione prematura. Discuti col team prima di procedere.

---

## Checkpoint riepilogativo

Per riferimento veloce, dopo ogni step principale devi poter rispondere "sì" a queste domande:

| Dopo lo step | Domanda |
|---|---|
| 1 (VO) | I VO compilano e seguono il pattern `Create() → Validation<T>`? |
| 2 (request) | Le request sono solo primitive nullable? |
| 3 (event) | Ogni operazione significativa ha il suo evento? |
| 4 (aggregate) | `Build`, `dotnet test` esistenti ancora verdi (no regressioni)? |
| 5 (test Domain) | Tutti i nuovi test passano? Numerosi nuovi test sono verdi? |
| 6 (Application) | Gli integration event NON contengono tipi di dominio? |
| 7 (test Application) | Round-trip JSON degli integration event funziona? |
| 8 (API) | I test di integrazione passano via `WebApplicationFactory`? |
| 9 (commit) | `dotnet build && dotnet test` chiude a 0 errori / 0 warning? |

Se anche solo una risposta è "no", **non procedere allo step successivo**. Risolvi prima.

---

## Esempio applicato: l'aggregate Product

La procedura descritta sopra è stata applicata per intero per introdurre l'aggregate `Product` nel repository. È **secondo aggregate reale**, oltre a `Customer`, ed esiste proprio per dimostrare che la convenzione è replicabile.

Cosa cercare nel codice se vuoi vedere ogni step "concretizzato":

| Step della guida | File reali nel repository |
|---|---|
| 1 — VO | `src/DddEntityContracts.Domain/Products/ProductValueObjects.cs` (ProductId, Sku, ProductName, ProductDescription, Money, ProductStatus enum) |
| 2 — Request | `src/DddEntityContracts.Domain/Products/ProductEventsAndRequests.cs` (CreateProductRequest, UpdateProductRequest) |
| 3 — Event | stesso file (ProductCreated, ProductUpdated, ProductPublished, ProductArchived) |
| 4 — Aggregate | `src/DddEntityContracts.Domain/Products/Product.cs` |
| 5 — Test Domain | `tests/DddEntityContracts.Domain.Tests/Products/` (5 file, 59 test) |
| 6 — Application | `src/DddEntityContracts.Application/Contracts/IntegrationEvents/ProductPublishedIntegrationEvent.cs` + `src/DddEntityContracts.Application/Products/EventHandlers/ProductPublishedToIntegrationEventHandler.cs` |
| 7 — Test Application | `tests/DddEntityContracts.Application.Tests/Products/EventHandlers/ProductPublishedToIntegrationEventHandlerTests.cs` (3 test) |

Cosa rende `Product` interessante come esempio (in più rispetto a `Customer`):

- **Money è un VO con due campi correlati** (`Amount` + `Currency`) validati insieme via `Combine`.
- **Sku ha normalizzazione attiva** (uppercase) prima di passare il regex check.
- **Lifecycle a tre stati con vincoli direzionali** (`Draft → Published → Archived`, no ritorni), non un toggle binario come `Active ↔ Inactive`.
- **Operations con state-machine guards** (`Update` solo se `Draft`, `Publish` solo se `Draft + Description != null`, `Archive` solo se `Published`).
- **Cross-field invariant non triviale** (`Amount > 1000 → Description obbligatoria`) verificato sia in `Create` sia in `Update` sia in `ChangePrice`.
- **`BuildValidatedState` con 4 campi**, composto via doppio `Combine` annidato per accumulare oltre il limite di 3 argomenti.

---

## Riferimenti rapidi

- [Aggregate Authoring Template](../templates/aggregate-authoring-template.md) — lo scheletro di codice da copiare.
- [04 — Customer walkthrough](04-customer-walkthrough.md) — un aggregate completo già esistente, da prendere come esempio.
- [conventions/decide-apply.md](../conventions/decide-apply.md) — perché Decide/Apply, le regole non opinabili.
- [conventions/value-object-authoring.md](../conventions/value-object-authoring.md) — come scrivere un VO corretto.
- [adr/ADR-001](../adr/ADR-001-domain-create-update-convention.md) — la decisione architetturale formale.
- [07 — Strategia di test](07-testing.md) — come scrivere test che si leggano da soli.

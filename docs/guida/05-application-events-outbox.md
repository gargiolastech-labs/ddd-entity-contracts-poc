# 05 — Application, eventi e outbox

Questo documento spiega come gli **eventi di dominio** sollevati dall'aggregate escono dal Domain e diventano:

1. **side-effect interni** al boundary (es. invio email di benvenuto)
2. **integration events** pubblicati via Outbox verso l'esterno (es. CRM, data warehouse, broker)

Tutto questo avviene nel progetto `src/DddEntityContracts.Application/`. Il Domain non sa nulla di email, broker, outbox, né integration event.

---

## La separazione fondamentale

```
┌─────────────────┐  Domain Event   ┌──────────────────┐
│                 │ ──────────────► │                  │
│     Domain      │                 │   Application    │
│                 │                 │                  │
│ • Aggregate     │                 │ • Handlers       │
│ • Domain Event  │                 │ • Integration Ev │
│                 │                 │ • Outbox         │
└─────────────────┘                 └──────────────────┘
                                              │
                                              │ Integration Event (DTO stabile)
                                              ▼
                                    ┌──────────────────┐
                                    │  External world  │
                                    │ (CRM, DW, broker)│
                                    └──────────────────┘
```

| Concetto | Vive in | Forma | Stabilità |
|---|---|---|---|
| Domain event | Domain | Record con tipi di dominio (`CustomerId`, ecc.) | Cambia quando cambia il dominio |
| Integration event | Application | DTO con tipi primitivi (`Guid`, `string`) | Versionabile, retro-compatibile |
| Side-effect | Application | Effetto interno al boundary | Non versionabile |

**Regola di confine:**

> Un integration event non deve mai contenere `CustomerId`, `Email`, `CustomerName` o qualunque altro Value Object del dominio. Solo primitive.

Motivo: un integration event può essere consumato da un servizio scritto in un altro linguaggio, da un team senza accesso al codice del dominio, o da un legacy che non capisce un `CustomerId` strongly-typed.

---

## I contratti integration event

### Base

```csharp
public abstract record IntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    int Version);
```

`abstract record` con tre campi obbligatori per qualunque integration event:

- `EventId`: identificatore univoco dell'evento (per dedup downstream).
- `OccurredAtUtc`: timestamp di quando l'evento è stato prodotto.
- `Version`: schema version dell'evento. Permette evoluzione retro-compatibile.

### Concreti

```csharp
public sealed record CustomerRegisteredIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    int Version,
    Guid CustomerId)
    : IntegrationEvent(EventId, OccurredAtUtc, Version);
```

```csharp
public sealed record CustomerDeactivatedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    int Version,
    Guid CustomerId,
    string? Reason)
    : IntegrationEvent(EventId, OccurredAtUtc, Version);
```

Punti chiave:

- `Guid CustomerId`, non `CustomerId CustomerId`. Il VO viene **scomposto** al confine.
- `string? Reason` (non un VO `DeactivationReason`).
- `Version = 1` per ora. Quando lo schema cambia in modo breaking, si pubblica `Version = 2` come **nuovo evento DTO** (`CustomerDeactivatedIntegrationEventV2`) tenendo vivo il vecchio.

**Naming**: integration event nominati per "ciò che è successo dal punto di vista esterno", non per il nome dell'evento di dominio. `CustomerCreated` (domain) diventa `CustomerRegistered` (integration): il mondo esterno vede una registrazione, non una creazione tecnica.

---

## Il contratto handler

```csharp
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
```

Vive in Application, non in Domain. Il Domain non deve sapere che i suoi eventi vengono "handlati".

`in TEvent` (contravariant): un `IDomainEventHandler<CustomerCreated>` è registrato per il tipo specifico, non per `IDomainEvent` generico. Questo permette al DI container di risolvere tutti gli handler interessati a un dato evento.

Un singolo evento di dominio può avere **n handler** (zero-to-many). Ciascuno fa una cosa diversa.

---

## Gli handler attuali

Al momento sono registrati quattro handler:

| Dominio source | Handler | Esce verso |
|---|---|---|
| `CustomerCreated` | `CustomerCreatedWelcomeEmailHandler` | Side-effect interno (`ICustomerNotificationSender`) |
| `CustomerCreated` | `CustomerCreatedToIntegrationEventHandler` | Outbox (`CustomerRegistered.v1`) |
| `CustomerDeactivated` | `CustomerDeactivatedToIntegrationEventHandler` | Outbox (`CustomerDeactivated.v1`) |
| `ProductPublished` | `ProductPublishedToIntegrationEventHandler` | Outbox (`ProductPublished.v1`) |

Un singolo domain event può essere indirizzato a **più handler**: `CustomerCreated` ha sia il side-effect (welcome email) sia il mapping a integration event. Nulla impedisce di aggiungere altri handler in futuro (es. audit log, analytics) — il pattern scala one-to-many naturalmente.

### 1. CustomerCreatedWelcomeEmailHandler (side-effect)

```csharp
public sealed class CustomerCreatedWelcomeEmailHandler : IDomainEventHandler<CustomerCreated>
{
    private readonly ICustomerNotificationSender _notificationSender;

    public CustomerCreatedWelcomeEmailHandler(ICustomerNotificationSender notificationSender)
    {
        _notificationSender = notificationSender;
    }

    public Task HandleAsync(CustomerCreated domainEvent, CancellationToken cancellationToken = default)
        => _notificationSender.SendWelcomeEmailAsync(domainEvent.CustomerId.Value, cancellationToken);
}
```

Ascolta `CustomerCreated` e produce un **side-effect interno** al boundary: invia un'email di benvenuto.

- Non produce alcun integration event.
- Non scrive in outbox.
- Riceve `ICustomerNotificationSender` via DI: è un'astrazione, l'implementazione SMTP reale è infrastructure.

Il `domainEvent.CustomerId.Value` è il `Guid` interno al VO `CustomerId`. Il sender accetta `Guid`, non `CustomerId`: il VO di dominio non attraversa il confine.

### 2. CustomerCreatedToIntegrationEventHandler

```csharp
public Task HandleAsync(CustomerCreated domainEvent, CancellationToken cancellationToken = default)
{
    var customerId = domainEvent.CustomerId.Value;

    var integrationEvent = new CustomerRegisteredIntegrationEvent(
        EventId: Guid.NewGuid(),
        OccurredAtUtc: DateTimeOffset.UtcNow,
        Version: 1,
        CustomerId: customerId);

    var message = new OutboxMessage(
        Id: Guid.NewGuid(),
        Type: "CustomerRegistered.v1",
        Payload: JsonSerializer.Serialize(integrationEvent),
        OccurredAtUtc: integrationEvent.OccurredAtUtc,
        DeduplicationKey: $"CustomerRegistered:{customerId}");

    return _outboxWriter.EnqueueAsync(message, cancellationToken);
}
```

Ascolta lo stesso `CustomerCreated` e produce un **integration event**: `CustomerRegisteredIntegrationEvent`.

Tre azioni:

1. **Mapping**: scompone `domainEvent.CustomerId` in `Guid`. Crea il DTO.
2. **Serializzazione**: `System.Text.Json` sul DTO. Il payload è `string` JSON.
3. **Accodamento**: `IOutboxWriter.EnqueueAsync`. Il messaggio finisce nell'outbox.

**Type stabile**: `"CustomerRegistered.v1"` è un identificatore che un consumer downstream può usare per fare routing. Quando esce v2, il type cambia a `"CustomerRegistered.v2"`. Mai cambiare il significato di una `v1` esistente.

**Deduplication key**: `"CustomerRegistered:{customerId}"`. Garantisce che se lo stesso domain event viene redelivered (at-least-once), l'outbox store ne può scartare il duplicato.

### 3. CustomerDeactivatedToIntegrationEventHandler

```csharp
public Task HandleAsync(CustomerDeactivated domainEvent, CancellationToken cancellationToken = default)
{
    var customerId = domainEvent.CustomerId.Value;

    var integrationEvent = new CustomerDeactivatedIntegrationEvent(
        EventId: Guid.NewGuid(),
        OccurredAtUtc: DateTimeOffset.UtcNow,
        Version: 1,
        CustomerId: customerId,
        Reason: domainEvent.Reason);

    var message = new OutboxMessage(
        Id: Guid.NewGuid(),
        Type: "CustomerDeactivated.v1",
        Payload: JsonSerializer.Serialize(integrationEvent),
        OccurredAtUtc: integrationEvent.OccurredAtUtc,
        DeduplicationKey: $"CustomerDeactivated:{customerId}");

    return _outboxWriter.EnqueueAsync(message, cancellationToken);
}
```

Stessa struttura. Trade-off documentato nel codice: la `DeduplicationKey` è stabile per-customer. Se un customer viene disattivato, riattivato, e disattivato di nuovo, lo store outbox potrebbe scartare la seconda disattivazione. In produzione: chiave basata su `EventId` o `(CustomerId, OccurredAtUtc)`.

### 4. ProductPublishedToIntegrationEventHandler

```csharp
public Task HandleAsync(ProductPublished domainEvent, CancellationToken cancellationToken = default)
{
    var productId = domainEvent.ProductId.Value;

    var integrationEvent = new ProductPublishedIntegrationEvent(
        EventId: Guid.NewGuid(),
        OccurredAtUtc: DateTimeOffset.UtcNow,
        Version: 1,
        ProductId: productId);

    var message = new OutboxMessage(
        Id: Guid.NewGuid(),
        Type: "ProductPublished.v1",
        Payload: JsonSerializer.Serialize(integrationEvent),
        OccurredAtUtc: integrationEvent.OccurredAtUtc,
        DeduplicationKey: $"ProductPublished:{productId}");

    return _outboxWriter.EnqueueAsync(message, cancellationToken);
}
```

Stesso pattern dei due handler precedenti, applicato all'aggregate `Product`. Pubblica `ProductPublished.v1` quando il dominio solleva `ProductPublished` (cioè dopo `Product.Publish()` ha avuto successo).

Nota: il dominio solleva `ProductPublished` **solo** nelle transizioni effettive Draft → Published. Se `Publish()` viene chiamato due volte di fila, la seconda è idempotente e non solleva alcun evento. Il numero di integration event pubblicati riflette quindi il numero di transizioni reali, non il numero di chiamate.

---

## Il pattern Outbox

```csharp
public interface IOutboxWriter
{
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

public sealed record OutboxMessage(
    Guid Id,
    string Type,
    string Payload,
    DateTimeOffset OccurredAtUtc,
    string? DeduplicationKey = null);
```

### Cos'è l'Outbox

L'Outbox è un pattern transazionale: invece di scrivere su DB e pubblicare su broker in due step separati (con rischio di inconsistenza), si scrive il messaggio in una tabella `outbox` **nella stessa transazione** del cambio di stato del dominio. Un worker separato legge la tabella e pubblica sul broker.

```
┌──────────────────────────────────────────────────────────┐
│ Transazione DB                                           │
│                                                          │
│ 1. UPDATE customer SET status='inactive' WHERE id=X      │
│ 2. INSERT INTO outbox (id, type, payload, ...) VALUES ...│
│                                                          │
└──────────────────────────────────────────────────────────┘
                          │
                          ▼
            ┌──────────────────────────┐
            │  Worker outbox dispatcher │
            │  (separato, asincrono)    │
            └──────────────┬────────────┘
                           │
                           ▼
                    Broker / HTTP / ecc.
```

### Cosa fa nel PoC

Nel PoC non esiste un worker, né un broker, né un DB. C'è solo `InMemoryOutboxWriter` (in `src/DddEntityContracts.Api/Stubs/ApplicationStubs.cs`) che accumula i messaggi in una lista thread-safe per ispezione.

**L'astrazione `IOutboxWriter` è quella che conta**: è il contratto che gli handler vedono. Domani si sostituisce con `SqlOutboxWriter`, `EventStoreOutboxWriter`, ecc. senza toccare nulla del Domain o degli handler.

### Idempotenza

L'Outbox lavora in **at-least-once delivery**: lo stesso messaggio può essere consegnato più volte. Il `DeduplicationKey` permette al consumer (o allo store outbox) di scartare i duplicati.

Nel PoC, il test `IntegrationEventHandler_IsIdempotent_OnRedelivery` verifica che lo stesso domain event redelivered due volte produca due `OutboxMessage` con la **stessa** `DeduplicationKey`. La dedup effettiva è responsabilità dello storage.

---

## Le astrazioni di notification

```csharp
public interface ICustomerNotificationSender
{
    Task SendWelcomeEmailAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task SendCustomerDeactivatedNotificationAsync(
        Guid customerId,
        string? reason,
        CancellationToken cancellationToken = default);
}
```

Vive in `Application.Abstractions.Notifications`. È un contratto **business-shaped**, non un'astrazione generica `IEmailSender`. I metodi descrivono **cosa** è successo nel boundary applicativo ("manda welcome a questo customer"), non **come** ("manda un'email con SMTP").

Implementazione PoC: `NoOpCustomerNotificationSender`. Produzione: implementazione SMTP/SendGrid/ecc.

---

## Registrazione DI

```csharp
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventHandler<CustomerCreated>, CustomerCreatedWelcomeEmailHandler>();
        services.AddScoped<IDomainEventHandler<CustomerCreated>, CustomerCreatedToIntegrationEventHandler>();
        services.AddScoped<IDomainEventHandler<CustomerDeactivated>, CustomerDeactivatedToIntegrationEventHandler>();
        services.AddScoped<IDomainEventHandler<ProductPublished>, ProductPublishedToIntegrationEventHandler>();
        return services;
    }
}
```

Estensione su `IServiceCollection` invocata dall'host. Il pattern "due handler registrati sullo stesso `IDomainEventHandler<CustomerCreated>`" è legittimo: il container risolverà entrambi quando un dispatcher chiederà la collezione.

Il dispatcher in sé **non esiste ancora** nel PoC: gli handler oggi sono testati in isolamento. Quando verrà aggiunto, sarà responsabilità dell'infrastructure layer (il worker outbox dispatcher tipicamente, o un middleware applicativo dopo `Customer.Save`).

---

## I test

I test sono in `tests/DddEntityContracts.Application.Tests/`:

| File | Cosa verifica |
|---|---|
| `Customers/EventHandlers/CustomerCreatedWelcomeEmailHandlerTests.cs` | Welcome email triggered, nessun outbox |
| `Customers/EventHandlers/CustomerCreatedToIntegrationEventHandlerTests.cs` | Mapping → integration event, type stabile, dedup key stabile su redelivery |
| `Customers/EventHandlers/CustomerDeactivatedToIntegrationEventHandlerTests.cs` | Mapping, reason preservata, payload con soli primitive |
| `Products/EventHandlers/ProductPublishedToIntegrationEventHandlerTests.cs` | Mapping ProductPublished, payload con primitive, idempotenza redelivery |

I test usano due fake in `Fakes/`:

```csharp
internal sealed class FakeOutboxWriter : IOutboxWriter
{
    public List<OutboxMessage> Messages { get; } = [];

    public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}
```

```csharp
internal sealed class FakeCustomerNotificationSender : ICustomerNotificationSender
{
    public List<(Guid CustomerId, string Method)> Calls { get; } = [];
    // ...
}
```

Niente mocking framework: fake hand-written, leggibili, debuggabili. Vedi [07 — Strategia di test](07-testing.md).

---

## Prossimo passo

Per capire come la pipeline HTTP traduce un `Result` fallito in ProblemDetails: [06 — API e ProblemDetails](06-api-problemdetails.md).

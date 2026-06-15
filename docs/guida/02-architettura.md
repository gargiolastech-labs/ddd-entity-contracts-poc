# 02 — Architettura

## Vista d'insieme

Il PoC è strutturato in tre layer concentrici. La direzione delle dipendenze va **dall'esterno verso il centro**: l'API conosce l'Application, l'Application conosce il Domain, il Domain non conosce nessuno.

```
┌─────────────────────────────────────────────────────────────┐
│                  DddEntityContracts.Api                     │
│                                                             │
│  Minimal API host                                           │
│  - Program.cs                                               │
│  - Behaviors/ResultToProblemDetailsBehavior                 │
│  - Stubs/InMemoryOutboxWriter, NoOpCustomerNotificationSender│
│                          │                                  │
└──────────────────────────┼──────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────────┐
│              DddEntityContracts.Application                 │
│                                                             │
│  - Abstractions/IDomainEventHandler                         │
│  - Abstractions/Outbox/IOutboxWriter, OutboxMessage         │
│  - Abstractions/Notifications/ICustomerNotificationSender   │
│  - Contracts/IntegrationEvents/*                            │
│  - Customers/EventHandlers/*                                │
│  - ApplicationServiceCollectionExtensions                   │
│                          │                                  │
└──────────────────────────┼──────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                DddEntityContracts.Domain                    │
│                                                             │
│  - SharedKernel/ (Result, Validation, Delta, Entity, Error) │
│  - Customers/ (Customer, CustomerValueObjects, Events)      │
│  - Products/ (Product, ProductValueObjects, Events)         │
│                                                             │
│   Nessun riferimento a NuGet esterni.                       │
│   Nessuna conoscenza di Application o Api.                  │
└─────────────────────────────────────────────────────────────┘
```

## I tre layer

### Domain (`src/DddEntityContracts.Domain`)

Il cuore del PoC. Contiene:

- **Shared Kernel**: primitive riusabili da qualunque aggregate (`Result`, `Validation`, `Delta`, `Entity`, `Error`, `IDomainEvent`, `IDomainCreatable`, `IDomainUpdatable`, `ValueObjectGuards`, `StronglyTypedId`).
- **Customers**: l'aggregate pilota `Customer` con i suoi Value Object (`CustomerName`, `Email`, `PhoneNumber`, `CustomerId`) e i suoi eventi di dominio (`CustomerCreated`, `CustomerUpdated`, `CustomerActivated`, `CustomerDeactivated`).
- **Products**: secondo aggregate `Product` con VO (`ProductId`, `Sku`, `ProductName`, `ProductDescription`, `Money`) e eventi (`ProductCreated`, `ProductUpdated`, `ProductPublished`, `ProductArchived`). Lifecycle a tre stati `Draft → Published → Archived`.

Vincoli architetturali del Domain:

| Vincolo | Verifica |
|---|---|
| Nessun riferimento NuGet esterno | `DddEntityContracts.Domain.csproj` non ha `<PackageReference>` |
| Nessun riferimento a `Microsoft.Extensions.*` | Stesso file |
| Nessun riferimento ad ASP.NET Core | Stesso file |
| Nessuna reflection | Grep per `typeof`, `GetType`, `Activator` deve restituire risultati limitati a `nameof()` o equivalenti |
| Nessuna astrazione generica prematura | Nessun `Repository<T>`, `Service<T>`, `Handler<T>` |
| `Nullable enable` + `TreatWarningsAsErrors true` | `DddEntityContracts.Domain.csproj` |

Approfondimento: [03 — Shared Kernel](03-shared-kernel.md), [04 — Customer walkthrough](04-customer-walkthrough.md).

### Application (`src/DddEntityContracts.Application`)

Orchestrazione e mediation tra Domain e mondo esterno. Contiene:

- **Abstractions**: contratti che il Domain non deve conoscere (`IDomainEventHandler`, `IOutboxWriter`, `ICustomerNotificationSender`).
- **Contracts**: integration events DTO (`CustomerRegisteredIntegrationEvent`, `CustomerDeactivatedIntegrationEvent`, `ProductPublishedIntegrationEvent`) che vivono **solo qui**, mai nel Domain.
- **EventHandlers**: handler che intercettano eventi di dominio e producono o side-effect interni o messaggi outbox. Organizzati per aggregate (`Customers/EventHandlers/`, `Products/EventHandlers/`).
- **ServiceCollection extension**: `AddApplicationEventHandlers()` per registrare gli handler nel DI container.

Vincoli architetturali dell'Application:

| Vincolo | Verifica |
|---|---|
| Riferisce solo il Domain | `DddEntityContracts.Application.csproj` |
| Nessun riferimento ad ASP.NET Core | Stesso file |
| Tutti gli integration events ereditano da `IntegrationEvent` astratto con `EventId/OccurredAtUtc/Version` | `Contracts/IntegrationEvents/IntegrationEvent.cs` |
| Nessun integration event espone un Value Object del Domain | Grep su `Contracts/IntegrationEvents/` |

Approfondimento: [05 — Application, eventi e outbox](05-application-events-outbox.md).

### Api (`src/DddEntityContracts.Api`)

Host minimal API che espone gli endpoint e implementa la traduzione `Result → ProblemDetails`. Contiene:

- **Program.cs**: composition root.
- **Behaviors**: `ResultToProblemDetailsBehavior` (endpoint filter) intercetta `Result` falliti e li converte in RFC 7807.
- **Stubs**: implementazioni in-memory di `IOutboxWriter` e `ICustomerNotificationSender` per dev runs. **Non sono produzione**: in produzione vengono sostituite con DB-backed outbox e mailer reali.

Approfondimento: [06 — API e ProblemDetails](06-api-problemdetails.md).

## Mappa dei progetti e dei test

```
src/
  DddEntityContracts.Domain/             → DddEntityContracts.Domain.Tests/
  DddEntityContracts.Application/        → DddEntityContracts.Application.Tests/
  DddEntityContracts.Api/                → DddEntityContracts.Api.Tests/
```

Ogni progetto di produzione ha esattamente un progetto di test omonimo. I test non condividono fixture cross-project.

| Test project | Tipo predominante | Cosa verifica |
|---|---|---|
| `Domain.Tests` | Unit | Primitive, aggregate, value object, idempotenza eventi |
| `Application.Tests` | Unit (con fake) | Mapping domain event → integration event, side-effect |
| `Api.Tests` | Integration via `WebApplicationFactory<Program>` | Pipeline HTTP, ProblemDetails |

## Direzione delle dipendenze — perché conta

La regola "Domain non conosce Application, Application non conosce Api" non è dogma. Concretamente significa:

1. Se domani sostituiamo Minimal API con gRPC o un worker headless, il Domain non cambia.
2. Se sostituiamo l'in-memory outbox con uno store SQL transazionale, il Domain non cambia.
3. Se un domain event aggiunge un campo, gli integration events restano **stabili** (versionabili indipendentemente) perché vivono in un altro modulo.

Ogni volta che si è tentati di mettere `using Microsoft.AspNetCore...` nel Domain o un `using Domain.Customers` nel contratto integration, fermarsi.

## File chiave

| File | Cosa contiene |
|---|---|
| `DddEntityContracts.sln` | Solution classica (`.sln`, non `.slnx`) |
| `src/.../Domain.csproj` | Zero package reference |
| `src/.../Application.csproj` | Solo `Microsoft.Extensions.DependencyInjection` |
| `src/.../Api.csproj` | `Microsoft.NET.Sdk.Web` |
| `CLAUDE.md` | Istruzioni di collaborazione AI (non documentazione di prodotto) |

## Prossimo passo

Per imparare le primitive che il Domain mette a disposizione: [03 — Shared Kernel](03-shared-kernel.md).

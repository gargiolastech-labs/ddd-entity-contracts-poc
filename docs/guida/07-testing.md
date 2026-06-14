# 07 — Strategia di test

Questo documento descrive **cosa testare**, **dove testare**, e **come scrivere un test che si legga senza commenti**.

Al momento il PoC ha 74 test totali (61 Domain + 7 Application + 3 Api), tutti verdi. Le righe guida che seguono sono ricavate dai pattern adottati nei test esistenti.

---

## Layer e tipo di test

| Progetto di test | Tipo predominante | Mocking framework | Fixture |
|---|---|---|---|
| `DddEntityContracts.Domain.Tests` | Unit | Nessuno | Nessuna (test self-contained) |
| `DddEntityContracts.Application.Tests` | Unit con fake hand-written | Nessuno | Nessuna |
| `DddEntityContracts.Api.Tests` | Integration | Nessuno | `WebApplicationFactory<Program>` |

**Nessun framework di mocking** (Moq, NSubstitute, FakeItEasy, ecc.). Le ragioni:

- I fake hand-written sono più leggibili.
- Si debugga il test, non il setup del mock.
- Non richiede formazione su un'API esterna per chi entra nel codebase.

**Niente FluentValidation/AutoFixture/Bogus.** Test minimali, dipendenze minime.

---

## Stack di test

```
xUnit 2.9.2                  — test runner
FluentAssertions 8.10.0      — assertion library
coverlet.collector 6.0.2     — coverage
Microsoft.NET.Test.Sdk 17.12  — SDK
Microsoft.AspNetCore.Mvc.Testing 9.0.1  — solo Api.Tests, per WebApplicationFactory
```

---

## Convenzioni di naming

### Nome della classe

`<TipoSottoTest>Tests` per test unitari.

Esempi:

- `ValidationTests` (testa `Validation<T>`)
- `CustomerCreationTests` (testa il path di creazione dell'aggregate Customer)
- `CustomerCreatedToIntegrationEventHandlerTests` (testa l'handler)

### Nome del metodo

`<Soggetto>_<Scenario>_<Comportamento>`.

Esempi reali:

```
Validation_Failure_WithEmptyErrors_ThrowsInvalidOperation
Validation_Combine_AccumulatesAllErrors
Create_WithValidRequest_RaisesCustomerCreatedEvent
Create_WithMultipleInvalidFields_AccumulatesErrors
CustomerId_From_EmptyGuid_Throws
Update_InvalidField_NoPartialMutation_NoEvents
Activate_AlreadyActive_NoEvent
Deactivate_WithReason_PreservesReasonOnEvent
ChangeEmail_SameValue_NoEvent
Behavior_AggregatedValidationErrors_AreAllSurfacedAsProblemDetails
CustomerCreated_TriggersWelcomeEmail
IntegrationEventHandler_IsIdempotent_OnRedelivery
```

Il nome deve raccontare il test in una riga: leggendolo, dovresti sapere cosa fallisce se diventa rosso.

---

## Struttura AAA

Arrange / Act / Assert, separati da righe vuote:

```csharp
[Fact]
public async Task CustomerCreated_TriggersWelcomeEmail()
{
    // Arrange
    var customerId = CustomerId.New();
    var domainEvent = new CustomerCreated(customerId);
    var notificationSender = new FakeCustomerNotificationSender();
    var sut = new CustomerCreatedWelcomeEmailHandler(notificationSender);

    // Act
    await sut.HandleAsync(domainEvent);

    // Assert
    notificationSender.Calls.Should().ContainSingle();
    notificationSender.Calls[0].Method.Should().Be("SendWelcomeEmailAsync");
    notificationSender.Calls[0].CustomerId.Should().Be(customerId.Value);
}
```

I commenti `// Arrange`, `// Act`, `// Assert` sono opzionali ma utili per test lunghi.

`sut` (System Under Test) è la convenzione per la variabile che rappresenta l'oggetto sotto test, quando aiuta a distinguerlo dalle dipendenze.

---

## Tipi di test per layer

### Domain.Tests

#### Test sui Value Object

Verificano: validazione, normalizzazione (trim, lowercase), equality, hash.

```csharp
[Fact]
public void Email_Create_TrimsAndLowercases()
{
    var result = Email.Create("  ALICE@Example.COM  ").ToResult();
    result.IsSuccess.Should().BeTrue();
    result.Value.Value.Should().Be("alice@example.com");
}

[Fact]
public void Email_ValueEquality_SameNormalizedValueAreEqual()
{
    var e1 = Email.Create("ALICE@example.com").ToResult().Value;
    var e2 = Email.Create("alice@EXAMPLE.com").ToResult().Value;
    e1.Should().Be(e2);
}
```

#### Test sulle primitive (Result, Validation, Delta)

Verificano: invarianti (Failure deve avere errori), composizione (Bind, Map, Combine), comportamento accumulativo vs short-circuit.

```csharp
[Fact]
public void Validation_Combine_AccumulatesAllErrors()
{
    var v1 = Validation<string>.Failure(new Error("ERR001", "Error 1"));
    var v2 = Validation<int>.Failure(new Error("ERR002", "Error 2"));

    var combined = Validation<string>.Combine(v1, v2, (s, i) => s + i);

    combined.IsFailure.Should().BeTrue();
    combined.ToResult().Errors.Should().HaveCount(2);
}
```

#### Test sull'aggregate

Verificano: happy path, accumulazione errori, no partial mutation in caso di fallimento, idempotenza degli eventi, target edits.

```csharp
[Fact]
public void Update_InvalidField_NoPartialMutation_NoEvents()
{
    var customer = Customer.Create(new CreateCustomerRequest("Alice", "alice@example.com", null)).Value;
    var originalName = customer.Name.Value;

    var result = customer.Update(new UpdateCustomerRequest("Alice", "not-an-email", null));

    result.IsFailure.Should().BeTrue();
    customer.Name.Value.Should().Be(originalName);   // ← nessuna mutazione
    customer.DomainEvents.Should().BeEmpty();        // ← nessun evento
}
```

Test cruciale per il pattern Decide/Apply: dimostra che un fallimento in mezzo all'update **non lascia mai** l'aggregate in uno stato parziale.

---

### Application.Tests

Verificano: mapping domain event → integration event, side-effect, idempotenza, struttura del payload.

#### Fake hand-written in `Fakes/`

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

Il fake registra le chiamate. Il test poi asserisce su `Messages`.

#### Test del mapping

```csharp
[Fact]
public async Task CustomerCreated_MappedTo_CustomerRegisteredIntegrationEvent_EnqueuedToOutbox()
{
    var customerId = CustomerId.New();
    var domainEvent = new CustomerCreated(customerId);

    var outbox = new FakeOutboxWriter();
    var sut = new CustomerCreatedToIntegrationEventHandler(outbox);

    await sut.HandleAsync(domainEvent);

    outbox.Messages.Should().ContainSingle();
    var message = outbox.Messages[0];
    message.Type.Should().Be("CustomerRegistered.v1");
    message.DeduplicationKey.Should().Be($"CustomerRegistered:{customerId.Value}");

    var integrationEvent = JsonSerializer.Deserialize<CustomerRegisteredIntegrationEvent>(message.Payload);
    integrationEvent!.CustomerId.Should().Be(customerId.Value);
    integrationEvent.Version.Should().Be(1);
}
```

Round-trip del payload via `JsonSerializer.Deserialize`: verifica che il payload sia ben formato e che il VO sia stato correttamente scomposto in primitive.

#### Test dell'idempotenza

```csharp
[Fact]
public async Task IntegrationEventHandler_IsIdempotent_OnRedelivery()
{
    var customerId = CustomerId.New();
    var domainEvent = new CustomerCreated(customerId);

    var outbox = new FakeOutboxWriter();
    var sut = new CustomerCreatedToIntegrationEventHandler(outbox);

    await sut.HandleAsync(domainEvent);
    await sut.HandleAsync(domainEvent);   // ← redelivery

    outbox.Messages.Should().HaveCount(2);
    var keys = outbox.Messages.Select(m => m.DeduplicationKey).Distinct();
    keys.Should().ContainSingle("same domain event redelivered must produce the same deduplication key");
}
```

Il test verifica la **stabilità della dedup key**, non che il fake faccia dedup. La dedup effettiva è dello storage outbox in produzione.

---

### Api.Tests

#### WebApplicationFactory

```csharp
public class ResultToProblemDetailsBehaviorTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ResultToProblemDetailsBehaviorTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }
    // ...
}
```

`IClassFixture<WebApplicationFactory<Program>>` istanzia un singolo server in-memory condiviso tra i test della classe. `factory.CreateClient()` produce un `HttpClient` connesso senza socket reali.

#### Test HTTP completo

```csharp
[Fact]
public async Task Behavior_AggregatedValidationErrors_AreAllSurfacedAsProblemDetails()
{
    var response = await _client.GetAsync("/api/probe/validation-failure");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

    var json = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    root.GetProperty("status").GetInt32().Should().Be(400);
    root.GetProperty("title").GetString().Should().Be("Validation failed");
    root.GetProperty("errors").GetArrayLength().Should().Be(3);
}
```

Test end-to-end: HTTP request reale, response reale, parsing del JSON con `System.Text.Json.JsonDocument`.

---

## Cosa NON testare

Per restare lean:

- **Non testare il framework**: niente test su `JsonSerializer.Serialize`, su `Results.Ok`, su `Guid.NewGuid`.
- **Non testare i getter triviali**: `customer.Name.Value` è scontato; testa il comportamento, non l'accessor.
- **Non duplicare i test della stessa logica su layer diversi**: se `CustomerName.Create` ha 5 test sulla validazione, `CustomerCreationTests` ne può avere 1 per la propagazione, non altri 5.
- **Non scrivere test che descrivono l'implementazione**: testa il contratto (input → output), non come quel contratto è realizzato internamente.

---

## Cosa va sempre testato

| Cosa | Perché |
|---|---|
| Happy path | Documenta il comportamento corretto |
| Accumulazione errori | Garantisce che `Validation` non degeneri in short-circuit |
| Edge case (null, empty, Guid.Empty, oltre maxlength, ...) | Sono i punti dove il dominio sbaglia |
| Idempotenza eventi | Dimostra che `Update`/`Activate` no-op non sollevano eventi |
| No partial mutation | Dimostra che `Decide → Apply` rispetta l'invariante atomico |
| Round-trip JSON degli integration event | Garantisce che il payload sia consumabile downstream |
| `Result` failure → 400 ProblemDetails | Pipeline API |

---

## Come eseguire i test

```bash
dotnet test --configuration Release
```

Per un singolo progetto:

```bash
dotnet test tests/DddEntityContracts.Domain.Tests --configuration Release
```

Per un filtro:

```bash
dotnet test --filter "FullyQualifiedName~CustomerCreation"
```

---

## Quando un test fallisce

Procedura:

1. **Leggi il nome del test** — deve dirti già cosa è rotto.
2. **Non commentare il test**. Mai. Capisci perché fallisce.
3. **Se il comportamento è cambiato intenzionalmente**: aggiorna il test con un nome che rifletta il nuovo comportamento, e committalo nello stesso commit del cambio di codice.
4. **Se è una regressione**: il commit è da fixare, non il test.

---

## Prossimo passo

Per i termini DDD usati in tutto il codebase: [08 — Glossario](08-glossario.md).

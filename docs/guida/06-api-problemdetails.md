# 06 — API e ProblemDetails

Questo documento spiega come il layer API espone gli endpoint e come traduce in modo automatico i `Result` falliti del dominio in risposte HTTP RFC 7807 ProblemDetails.

L'intero layer vive in `src/DddEntityContracts.Api/` ed è basato su **Minimal API** (no MVC, no controller).

---

## Il pattern: filtro endpoint unico

La traduzione `Result fallito → 400 ProblemDetails` avviene **in un unico punto**: il filtro endpoint `ResultToProblemDetailsBehavior`. Tutti gli endpoint sotto `/api` lo applicano automaticamente.

```
HTTP request
     │
     ▼
┌─────────────────────────────────────────────┐
│ Endpoint handler                             │
│   produces: Result, Result<T>, o object Ok   │
└────────────────────┬─────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────┐
│ ResultToProblemDetailsBehavior (IEndpointFilter) │
│                                             │
│   if (value is Result { IsFailure: true })  │
│       → MapToProblemDetails(errors)         │
│   else                                      │
│       → pass-through                        │
└─────────────────────────────────────────────┘
                     │
                     ▼
              HTTP response
```

---

## L'endpoint filter

```csharp
public sealed class ResultToProblemDetailsBehavior : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var value = await next(context);

        if (value is Result { IsFailure: true } failed)
            return MapToProblemDetails(failed.Errors);

        return value;
    }

    private static IResult MapToProblemDetails(IReadOnlyCollection<Error> errors)
    {
        var errorDetails = JsonSerializer.SerializeToElement(
            errors.Select(e => new { code = e.Code, message = e.Message }));

        var errorCodes = JsonSerializer.SerializeToElement(
            errors.Select(e => e.Code));

        return Results.Problem(
            detail: "One or more validation errors occurred.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed",
            type: "https://httpstatuses.com/400",
            extensions: new Dictionary<string, object?>
            {
                ["errors"]     = errorDetails,
                ["errorCodes"] = errorCodes,
            });
    }
}
```

Tre comportamenti, in ordine di priorità:

1. **`Result` fallito** → tradotto in ProblemDetails 400 con `errors` e `errorCodes` come extension.
2. **Qualunque altro valore** (es. `Results.Ok(...)`, `Result.Success()` riconvertito, oggetto plain) → pass-through al pipeline standard.
3. **Eccezione** → non gestita qui. Va al `AddProblemDetails()` standard di ASP.NET (500 generico).

### Perché `IEndpointFilter` e non un middleware

Un middleware globale opera **dopo** che il pipeline ha già serializzato. Un endpoint filter opera sul valore di ritorno **prima** della serializzazione. Per intercettare `Result` come oggetto (non come JSON serializzato), serve il filter.

### Pre-serializzazione delle extensions

`JsonSerializer.SerializeToElement(...)` produce un `JsonElement`. Le extension di ProblemDetails accettano `object?`: passando `JsonElement` ci assicuriamo che la serializzazione finale sia consistente con `System.Text.Json` (no doppia serializzazione, no surprises con `Newtonsoft`).

---

## La forma della risposta

### Success

```http
HTTP/1.1 200 OK
Content-Type: application/json

{ "value": 42 }
```

### Failure (validation errors)

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "type": "https://httpstatuses.com/400",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": [
    { "code": "CustomerName.Required", "message": "Customer name is required." },
    { "code": "Email.InvalidFormat", "message": "Email format is invalid." },
    { "code": "PhoneNumber.InvalidFormat", "message": "Phone number format is invalid." }
  ],
  "errorCodes": [
    "CustomerName.Required",
    "Email.InvalidFormat",
    "PhoneNumber.InvalidFormat"
  ]
}
```

### Le due extension `errors` e `errorCodes`

| Campo | Tipo | Uso |
|---|---|---|
| `errors` | array di `{code, message}` | UI/debug: tutti i dettagli pronti da mostrare |
| `errorCodes` | array di stringhe | Client-side i18n / error mapping. Le chiavi sono stabili, i messaggi possono essere localizzati dal client |

Avere entrambi è ridondante? Sì, intenzionalmente. `errorCodes` è il contratto stabile per i client che vogliono fare error handling programmatico (`if (codes.includes("Email.InvalidFormat"))`). `errors` è il payload pronto-da-mostrare per chi vuole semplicità.

---

## Composition root

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();

builder.Services.AddApplicationEventHandlers();
builder.Services.AddSingleton<IOutboxWriter, InMemoryOutboxWriter>();
builder.Services.AddSingleton<ICustomerNotificationSender, NoOpCustomerNotificationSender>();

var app = builder.Build();

var api = app.MapGroup("/api")
    .AddEndpointFilter<ResultToProblemDetailsBehavior>();

api.MapGet("/probe/success", () =>
    Results.Ok(new { value = 42 }));

api.MapGet("/probe/validation-failure", () =>
    (object)Result.Failure(new Error[]
    {
        new("CustomerName.Required", "Customer name is required."),
        new("Email.InvalidFormat",   "Email format is invalid."),
        new("PhoneNumber.InvalidFormat", "Phone number format is invalid."),
    }));

app.Run();

public partial class Program { }
```

Punti chiave:

- `AddProblemDetails()` registra il provider standard di ASP.NET (necessario per eccezioni non gestite e altri 4xx/5xx).
- `AddApplicationEventHandlers()` viene dall'estensione Application.
- `AddSingleton<IOutboxWriter, InMemoryOutboxWriter>()` registra lo **stub** PoC. In produzione: `services.AddScoped<IOutboxWriter, SqlOutboxWriter>()`.
- `app.MapGroup("/api").AddEndpointFilter<ResultToProblemDetailsBehavior>()` applica il filter a tutti gli endpoint sotto `/api`. Endpoint fuori da `/api` non lo ereditano.
- `(object)Result.Failure(...)`: il cast a `object` serve perché Minimal API ottimizza la firma e senza cast il valore non passerebbe per il filter come previsto.
- `public partial class Program { }`: rende `Program` accessibile a `WebApplicationFactory<Program>` nei test di integrazione.

---

## Gli endpoint probe

Gli endpoint `/api/probe/success` e `/api/probe/validation-failure` esistono **solo per testare il filter**. Non rappresentano una feature di prodotto. Quando il PoC verrà esteso con endpoint Customer reali, questi probe rimarranno come smoke test del comportamento del filter, o verranno rimossi.

---

## Gli stub infrastructure

`src/DddEntityContracts.Api/Stubs/ApplicationStubs.cs` contiene:

```csharp
internal sealed class InMemoryOutboxWriter : IOutboxWriter
{
    private readonly object _gate = new();
    private readonly List<OutboxMessage> _messages = [];

    public IReadOnlyList<OutboxMessage> Messages
    {
        get { lock (_gate) return _messages.ToArray(); }
    }

    public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        lock (_gate) _messages.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class NoOpCustomerNotificationSender : ICustomerNotificationSender
{
    public Task SendWelcomeEmailAsync(Guid customerId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    // ...
}
```

Entrambi `internal sealed`. Non escono dal progetto Api. Il commento di testa nel file marca esplicitamente: "Production replaces these with real infrastructure (DB-backed outbox, SMTP, etc.)".

**Thread-safety**: `InMemoryOutboxWriter` registrato come `Singleton` e mutato da `List<T>` non sarebbe thread-safe sotto richieste concorrenti. Il lock su `_gate` chiude il rischio anche nello stub.

---

## I test di integrazione

I test sono in `tests/DddEntityContracts.Api.Tests/ResultToProblemDetailsBehaviorTests.cs` e usano `WebApplicationFactory<Program>`:

```csharp
public class ResultToProblemDetailsBehaviorTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ResultToProblemDetailsBehaviorTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Behavior_AggregatedValidationErrors_AreAllSurfacedAsProblemDetails() { /* ... */ }

    [Fact]
    public async Task Behavior_SuccessResult_PassesThrough() { /* ... */ }

    [Fact]
    public async Task Behavior_PreservesMachineReadableErrorCodes() { /* ... */ }
}
```

Tre comportamenti coperti:

1. **Failure aggregato**: tre errori in input, tre errori nella risposta (`errors.length === 3`).
2. **Success pass-through**: nessun ProblemDetails wrapping per le risposte success.
3. **errorCodes**: le chiavi machine-readable sono presenti e corrette.

`WebApplicationFactory` avvia il server in-memory e fornisce un `HttpClient` connesso. Test reali HTTP, non mocking del filter.

---

## Come si estende

Quando aggiungi un nuovo endpoint:

```csharp
api.MapPost("/customers", (CreateCustomerRequest request) =>
{
    var result = Customer.Create(request);

    return result.IsSuccess
        ? Results.Created($"/api/customers/{result.Value.Id}", new { id = result.Value.Id })
        : (object)result;   // ← il filter trasforma in ProblemDetails
});
```

Quando aggiungi un nuovo tipo di errore HTTP da gestire (es. 404 Not Found per "customer non trovato"):

- O lo gestisci con un `Result` failure con codice apposito (es. `Customer.NotFound`) e mappi nel filter con uno switch sul codice → status code.
- O ritorni direttamente un `Results.NotFound()` dall'endpoint quando il caso è "non c'era niente da cercare".

Il filter attuale è volutamente semplice: traduce sempre `IsFailure → 400`. Se la mappatura `code → status` cresce, va spostata in una `IErrorCodeToStatusMapper` e iniettata. Per il PoC non serve.

---

## Prossimo passo

Per scrivere o leggere i test su Domain, Application e Api: [07 — Strategia di test](07-testing.md).

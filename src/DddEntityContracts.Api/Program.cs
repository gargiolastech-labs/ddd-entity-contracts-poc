using Domain.SharedKernel;
using DddEntityContracts.Api.Behaviors;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();

var app = builder.Build();

// All routes under /api go through ResultToProblemDetailsBehavior.
var api = app.MapGroup("/api")
    .AddEndpointFilter<ResultToProblemDetailsBehavior>();

// ── Probe endpoints (integration-test infrastructure) ────────────────────────

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

// Required for WebApplicationFactory<Program> in integration tests.
public partial class Program { }

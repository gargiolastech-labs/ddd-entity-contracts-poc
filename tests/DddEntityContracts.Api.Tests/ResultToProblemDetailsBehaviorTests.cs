using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DddEntityContracts.Api.Tests;

public class ResultToProblemDetailsBehaviorTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ResultToProblemDetailsBehaviorTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

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

        // All three errors must be present — not just the first one.
        root.GetProperty("errors").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Behavior_SuccessResult_PassesThrough()
    {
        var response = await _client.GetAsync("/api/probe/success");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("value").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task Behavior_PreservesMachineReadableErrorCodes()
    {
        var response = await _client.GetAsync("/api/probe/validation-failure");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // errorCodes array: machine-readable codes for client-side i18n / error mapping.
        var codes = root.GetProperty("errorCodes")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        codes.Should().Contain("Email.InvalidFormat");
        codes.Should().Contain("CustomerName.Required");
        codes.Should().Contain("PhoneNumber.InvalidFormat");
        codes.Should().HaveCount(3);

        // errors array: full detail objects { code, message }.
        var firstError = root.GetProperty("errors")[0];
        firstError.GetProperty("code").GetString().Should().NotBeNullOrEmpty();
        firstError.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }
}

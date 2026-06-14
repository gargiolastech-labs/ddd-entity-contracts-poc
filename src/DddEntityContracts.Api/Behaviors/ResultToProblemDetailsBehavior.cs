using System.Text.Json;
using Domain.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace DddEntityContracts.Api.Behaviors;

// Intercepts any endpoint that returns a failed Domain Result and converts it to
// an RFC 7807 ProblemDetails response, preserving all accumulated error codes.
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

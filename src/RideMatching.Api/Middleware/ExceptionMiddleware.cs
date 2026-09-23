using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RideMatching.Api.Domain;

namespace RideMatching.Api.Middleware;

/// <summary>
/// Centralized exception handling. Maps domain exceptions to RFC 7807
/// ProblemDetails responses and never leaks stack traces to clients.
/// </summary>
public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception ex)
    {
        var (status, title, detail) = ex switch
        {
            DriverNotFoundException => (StatusCodes.Status404NotFound, "Driver not found", ex.Message),
            RideNotFoundException => (StatusCodes.Status404NotFound, "Ride not found", ex.Message),
            InvalidRideStateTransitionException => (StatusCodes.Status409Conflict, "Invalid ride state transition", ex.Message),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", ex.Message),
            OperationCanceledException => (499, "Request cancelled", "The request was cancelled."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", "An unexpected error occurred while processing the request.")
        };

        if (status >= 500)
        {
            _logger.LogError(ex, "Unhandled exception processing {Path}", context.Request.Path);
        }
        else
        {
            _logger.LogInformation("Request failed ({Status}) on {Path}: {Message}",
                status, context.Request.Path, ex.Message);
        }

        var problem = new ProblemDetails
        {
            Title = title,
            Status = status,
            Detail = detail,
            Instance = context.Request.Path
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}

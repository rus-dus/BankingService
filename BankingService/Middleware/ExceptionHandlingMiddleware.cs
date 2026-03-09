using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace BankingService.Middleware;

/// <summary>
/// Converts well-known domain exceptions into RFC 7807 ProblemDetails responses.
/// Keeps controllers free of try/catch boilerplate.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next   = next   ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);

            var (status, title) = ex switch
            {
                KeyNotFoundException     => (HttpStatusCode.NotFound,           "Resource not found"),
                InvalidOperationException => (HttpStatusCode.BadRequest,         "Business rule violation"),
                ArgumentException        => (HttpStatusCode.BadRequest,          "Invalid argument"),
                _                        => (HttpStatusCode.InternalServerError, "An unexpected error occurred")
            };

            var problem = new ProblemDetails
            {
                Status = (int)status,
                Title  = title,
                Detail = ex.Message,
            };

            context.Response.StatusCode  = (int)status;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
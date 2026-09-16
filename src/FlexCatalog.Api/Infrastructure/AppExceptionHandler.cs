using FlexCatalog.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FlexCatalog.Api.Infrastructure;

/// <summary>Translates the small set of app-level exceptions into ProblemDetails responses.</summary>
public sealed class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation Failed"),
            InvalidOperationException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            _ => (0, string.Empty),
        };

        if (statusCode == 0)
        {
            logger.LogError(exception, "Unhandled exception");
            return false;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
        }, cancellationToken);

        return true;
    }
}

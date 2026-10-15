using GateMint.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GateMint.Api;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            IdempotencyConflictException => (409, "Idempotency key conflict"),
            UnauthorizedAccessException => (403, "Access denied"),
            DomainException => (422, "Request could not be completed"),
            _ => (500, "An unexpected error occurred")
        };
        if (status == 500) logger.LogError(exception, "Unhandled request failure");
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title,
                Detail = status == 500 ? "Please try again later." : exception.Message }
        });
    }
}

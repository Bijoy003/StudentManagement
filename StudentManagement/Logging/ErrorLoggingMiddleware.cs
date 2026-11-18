using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudentManagement.Application.Interfaces;

namespace StudentManagement.Logging;

// Captures unhandled endpoint exceptions into the ErrorLogs table, then rethrows so the
// existing exception handler / developer page still produces the response. Registered
// after authentication so the failing user's id is available.
public class ErrorLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorLoggingMiddleware> _logger;

    public ErrorLoggingMiddleware(RequestDelegate next, ILogger<ErrorLoggingMiddleware> logger)
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
        catch (Exception exception)
        {
            // A client disconnect is not an application error.
            if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }

            try
            {
                using var scope = context.RequestServices.CreateScope();
                var errorLog = scope.ServiceProvider.GetRequiredService<IErrorLogService>();
                await errorLog.LogAsync(
                    context.Request.Path.Value ?? "/",
                    exception,
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
                    CancellationToken.None);
            }
            catch (Exception loggingException)
            {
                _logger.LogWarning(loggingException, "Error logging middleware could not persist the failure.");
            }

            throw;
        }
    }
}

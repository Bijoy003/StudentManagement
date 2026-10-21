using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace StudentManagement.Logging
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;
        private const string CorrelationIdHeader = "X-Correlation-ID";
        private const string CorrelationIdLogProperty = "CorrelationId";

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out var headerValue)
                ? headerValue.ToString()
                : Activity.Current?.Id ?? Guid.NewGuid().ToString();

            context.Response.Headers[CorrelationIdHeader] = correlationId;

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                [CorrelationIdLogProperty] = correlationId,
                ["RequestPath"] = context.Request.Path.Value ?? string.Empty,
                ["RequestMethod"] = context.Request.Method,
                ["TraceId"] = Activity.Current?.TraceId.ToString() ?? string.Empty,
                ["SpanId"] = Activity.Current?.SpanId.ToString() ?? string.Empty
            });

            _logger.LogInformation("HTTP {Method} {Path} started", context.Request.Method, context.Request.Path);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "HTTP {Method} {Path} completed in {ElapsedMs}ms with status {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    stopwatch.ElapsedMilliseconds,
                    context.Response.StatusCode);
            }
        }
    }
}